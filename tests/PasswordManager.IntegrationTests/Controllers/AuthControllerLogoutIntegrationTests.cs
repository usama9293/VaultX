using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;
using Xunit;

namespace PasswordManager.IntegrationTests.Controllers;

public class AuthControllerLogoutIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthControllerLogoutIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    private async Task RegisterUserAsync(string email, string password)
    {
        var registerRequest = new RegisterUserRequest(email, password, password);
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
    }

    private async Task<string> LoginAndGetRefreshTokenAsync(string email, string password)
    {
        var loginRequest = new LoginRequest(email, password);
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var rawToken = setCookie.Split(';')[0]["refreshToken=".Length..];
        return rawToken;
    }

    [Fact]
    public async Task Logout_ValidSession_Returns204AndRevokesTokenInDatabase()
    {
        // Arrange
        const string email = "logout.valid@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Empty(responseBody);

        // Verify database state: token is revoked
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);

        var tokenRecord = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        Assert.NotNull(tokenRecord);
        Assert.True(tokenRecord.IsRevoked);
        Assert.False(tokenRecord.IsActive);
        Assert.NotNull(tokenRecord.RevokedAt);
        Assert.True(tokenRecord.RevokedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Logout_InstructsBrowserToDeleteRefreshTokenCookie()
    {
        // Arrange
        const string email = "logout.cookie.deletion@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));

        var setCookieHeader = response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(setCookieHeader);

        // Verify cookie clearing properties
        Assert.Contains("path=/api/auth", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookieHeader, StringComparison.OrdinalIgnoreCase);

        // Must instruct deletion (expired timestamp or max-age=0)
        Assert.True(
            setCookieHeader.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            setCookieHeader.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Logout_MissingCookie_Returns204NoContentWithoutModifyingDatabase()
    {
        // Arrange
        const string email = "logout.missing.cookie@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        // Act: Logout without any cookie header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Database session remains active
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);

        var tokenRecord = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        Assert.NotNull(tokenRecord);
        Assert.True(tokenRecord.IsActive);
        Assert.Null(tokenRecord.RevokedAt);
    }

    [Fact]
    public async Task Logout_UnknownOrTamperedToken_Returns204NoContentWithoutModifyingExistingSessions()
    {
        // Arrange
        const string email = "logout.tampered.token@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        // Act: Send a fake/tampered refresh token cookie
        const string fakeToken = "completely-bogus-and-tampered-refresh-token-value-99999";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={fakeToken}");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // User's genuine session remains active and unrevoked
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var genuineHash = tokenService.HashRefreshToken(rawRefreshToken);

        var tokenRecord = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == genuineHash);
        Assert.NotNull(tokenRecord);
        Assert.True(tokenRecord.IsActive);
        Assert.Null(tokenRecord.RevokedAt);
    }

    [Fact]
    public async Task Logout_AlreadyRevokedSession_Returns204NoContentIdempotently()
    {
        // Arrange
        const string email = "logout.already.revoked@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        // First logout - revokes session
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request1.Headers.Add("Cookie", $"refreshToken={rawRefreshToken}");
        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.NoContent, response1.StatusCode);

        DateTime? firstRevokedAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);
            var tokenRecord = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == tokenHash);
            firstRevokedAt = tokenRecord.RevokedAt;
            Assert.NotNull(firstRevokedAt);
        }

        // Second logout with same token - idempotent
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request2.Headers.Add("Cookie", $"refreshToken={rawRefreshToken}");
        var response2 = await _client.SendAsync(request2);

        Assert.Equal(HttpStatusCode.NoContent, response2.StatusCode);

        // Assert timestamp did not get modified or corrupted
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);
            var tokenRecord = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == tokenHash);
            Assert.Equal(firstRevokedAt, tokenRecord.RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_ExpiredSession_Returns204NoContentWithoutReactivation()
    {
        // Arrange: manually insert an expired token into the database
        const string email = "logout.expired.session@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);

        const string rawExpiredToken = "raw-expired-test-token-value-123456";
        string expiredTokenHash;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var user = await db.Users.FirstAsync(u => u.Email == email);

            expiredTokenHash = tokenService.HashRefreshToken(rawExpiredToken);

            // Create token with future expiry, then update expiry in db directly to past
            var token = new RefreshToken(user.Id, expiredTokenHash, DateTime.UtcNow.AddMinutes(5));
            await db.RefreshTokens.AddAsync(token);
            await db.SaveChangesAsync();

            // Set expiry in the past directly in DB
            token = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == expiredTokenHash);
            typeof(RefreshToken).GetProperty(nameof(RefreshToken.ExpiresAt))!
                .SetValue(token, DateTime.UtcNow.AddDays(-1));
            await db.SaveChangesAsync();
        }

        // Act: Logout with the expired token
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={rawExpiredToken}");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify token is still expired, not reactivated, and RevokedAt was not modified
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenRecord = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == expiredTokenHash);
            Assert.True(tokenRecord.IsExpired);
            Assert.False(tokenRecord.IsActive);
            Assert.Null(tokenRecord.RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_MultipleSessions_RevokesOnlyTargetSession_PreservingOtherSessions()
    {
        // Arrange
        const string email = "logout.session.isolation@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);

        // Create Session A
        var tokenA = await LoginAndGetRefreshTokenAsync(email, password);
        // Create Session B (independent login)
        var tokenB = await LoginAndGetRefreshTokenAsync(email, password);

        Assert.NotEqual(tokenA, tokenB);

        // Verify both sessions are currently active in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hashA = tokenService.HashRefreshToken(tokenA);
            var hashB = tokenService.HashRefreshToken(tokenB);

            var recA = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashA);
            var recB = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashB);

            Assert.True(recA.IsActive);
            Assert.True(recB.IsActive);
        }

        // Act: Logout Session A only
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={tokenA}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert: Session A is revoked, Session B is STILL active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hashA = tokenService.HashRefreshToken(tokenA);
            var hashB = tokenService.HashRefreshToken(tokenB);

            var recA = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashA);
            var recB = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashB);

            // Session A must be revoked
            Assert.True(recA.IsRevoked);
            Assert.False(recA.IsActive);
            Assert.NotNull(recA.RevokedAt);

            // Session B must REMAIN active
            Assert.False(recB.IsRevoked);
            Assert.True(recB.IsActive);
            Assert.Null(recB.RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_Response_NeverExposesSensitiveData()
    {
        // Arrange
        const string email = "logout.sensitive.leak@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterUserAsync(email, password);
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Empty(body);
        Assert.DoesNotContain(rawRefreshToken, body);
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("$pbkdf2", body);
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Logout_UnsupportedHttpMethods_Return405MethodNotAllowed(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/auth/logout");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Logout_UnexpectedException_ReturnsSafe500ProblemDetailsWithoutInternalDisclosure()
    {
        var throwingClient = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll(services, typeof(PasswordManager.Application.Features.Authentication.Logout.ILogoutUserHandler));
                services.AddScoped<PasswordManager.Application.Features.Authentication.Logout.ILogoutUserHandler, ThrowingLogoutHandler>();
            });
        }).CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", "refreshToken=some-token");

        var response = await throwingClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("An error occurred while processing your request.", root.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("detail").GetString());

        // Zero disclosure of exception message, stack trace, or server details
        Assert.DoesNotContain("Sensitive DB Connection String", body);
        Assert.DoesNotContain("secretpass", body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    private sealed class ThrowingLogoutHandler : PasswordManager.Application.Features.Authentication.Logout.ILogoutUserHandler
    {
        public Task HandleAsync(PasswordManager.Application.Features.Authentication.Logout.LogoutCommand command, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Sensitive DB Connection String: Server=secret;Database=vault;User=admin;Password=secretpass;");
        }
    }
}
