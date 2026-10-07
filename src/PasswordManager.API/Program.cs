using System.Text;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using PasswordManager.API.Middleware;
using PasswordManager.API.Services;
using PasswordManager.Application;
using PasswordManager.Application.Common.Security;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Infrastructure;

namespace PasswordManager.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddControllers();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();

        builder.Services.AddOptions<JwtSettings>()
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.SecretKey)
                    && Encoding.UTF8.GetByteCount(settings.SecretKey) >= 32,
                "JwtSettings:SecretKey must be configured with at least 32 UTF-8 bytes using a secure configuration provider.")
            .ValidateOnStart();

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                ?? new JwtSettings();
            var secretKey = jwtSettings.SecretKey;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = !string.IsNullOrWhiteSpace(jwtSettings.Issuer) ? jwtSettings.Issuer : "VaultX.API",
                ValidateAudience = true,
                ValidAudience = !string.IsNullOrWhiteSpace(jwtSettings.Audience) ? jwtSettings.Audience : "VaultX.Client",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        builder.Services.AddAuthorization();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var retrySeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    context.HttpContext.Response.Headers.RetryAfter =
                        retrySeconds.ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "The request rate limit has been exceeded.",
                    Type = "https://tools.ietf.org/html/rfc6585#section-4"
                };
                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(problemDetails),
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => CreateSlidingWindowOptions(
                        builder.Configuration,
                        "RateLimiting:Global",
                        permitLimit: 300,
                        windowSeconds: 60,
                        segmentsPerWindow: 6)));

            options.AddPolicy("login", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => CreateSlidingWindowOptions(
                        builder.Configuration,
                        "RateLimiting:Login",
                        permitLimit: 10,
                        windowSeconds: 60,
                        segmentsPerWindow: 6)));

            options.AddPolicy("registration", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => CreateSlidingWindowOptions(
                        builder.Configuration,
                        "RateLimiting:Registration",
                        permitLimit: 5,
                        windowSeconds: 3600,
                        segmentsPerWindow: 12)));

            options.AddPolicy("refresh", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => CreateSlidingWindowOptions(
                        builder.Configuration,
                        "RateLimiting:Refresh",
                        permitLimit: 30,
                        windowSeconds: 60,
                        segmentsPerWindow: 6)));
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("FrontendDevPolicy", policy =>
            {
                policy.WithOrigins("http://localhost:5173", "https://localhost:5173", "http://localhost:3000")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestBodySizeLimitMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseRouting();
        app.UseCors("FrontendDevPolicy");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapGet("/", () => Results.Ok(new
        {
            message = "PasswordManager API is running.",
            status = "healthy"
        }));

        app.Run();
    }

    private static string GetRateLimitPartitionKey(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null)
        {
            return "unknown";
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        return remoteAddress.ToString();
    }

    private static SlidingWindowRateLimiterOptions CreateSlidingWindowOptions(
        IConfiguration configuration,
        string sectionPath,
        int permitLimit,
        int windowSeconds,
        int segmentsPerWindow)
    {
        return new SlidingWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue($"{sectionPath}:PermitLimit", permitLimit),
            Window = TimeSpan.FromSeconds(
                configuration.GetValue($"{sectionPath}:WindowSeconds", windowSeconds)),
            SegmentsPerWindow = configuration.GetValue(
                $"{sectionPath}:SegmentsPerWindow",
                segmentsPerWindow),
            QueueLimit = 0,
            AutoReplenishment = true
        };
    }
}
