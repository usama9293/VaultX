
using PasswordManager.API.Middleware;
using PasswordManager.Application;
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

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.MapControllers();

        app.MapGet("/", () => Results.Ok(new
        {
            message = "PasswordManager API is running.",
            status = "healthy"
        }));

        app.Run();
    }
}
