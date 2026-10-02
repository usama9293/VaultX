using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Infrastructure.Persistence;
using Xunit;

namespace PasswordManager.IntegrationTests.Integration;

public class LoginIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public LoginIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> RegisterUserViaApiAsync(string email, string password)
    {
        var request = new RegisterUserRequest(email, password, password);
        return await _client.PostAsJsonAsync("/api/auth/register", request);
    }

    [Fact]
    public async Task Registration_Then_Login_FullFlow_Succeeds_WithDatabaseVerification_AndHttpOnlyCookie()
    {
        // 1. Account Creation (Registration -> Database Persistence)
        const string email = "e2e.flow.success@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";

        var registerResponse = await RegisterUserViaApiAsync(email, password);
        Assert.True(
            registerResponse.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Registration failed with unexpected status: {registerResponse.StatusCode}");

        // 2. Execute Real HTTP POST /api/auth/login
        var loginPayload = new LoginRequest(email, password);
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);

        // 3. Verify HTTP 200 OK and response schema contract
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var body = await loginResponse.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.TryGetProperty("accessToken", out var accessTokenProp));
        var accessToken = accessTokenProp.GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        Assert.True(root.TryGetProperty("expiresAt", out var expiresAtProp));
        Assert.True(expiresAtProp.GetDateTime() > DateTime.UtcNow);

        // 4. Verify Cookie Security Attributes
        Assert.True(loginResponse.Headers.Contains("Set-Cookie"));
        var setCookieHeader = loginResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(setCookieHeader);

        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookieHeader, StringComparison.OrdinalIgnoreCase);

        // Extract raw token from cookie to verify cryptographic storage in database
        var rawCookieToken = setCookieHeader.Split(';')[0]["refreshToken=".Length..];
        Assert.False(string.IsNullOrWhiteSpace(rawCookieToken));

        // 5. Database Verification (PostgreSQL / Test DB)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == email);

        Assert.NotNull(user);
        Assert.NotEmpty(user.RefreshTokens);

        var latestRefreshToken = user.RefreshTokens.OrderByDescending(t => t.CreatedAt).First();

        // 6. Cryptographic Hash Verification: Database contains ONLY SHA-256 hash, NEVER raw token
        var expectedHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawCookieToken));
        var expectedHashHex = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        Assert.Equal(expectedHashHex, latestRefreshToken.TokenHash);
        Assert.NotEqual(rawCookieToken, latestRefreshToken.TokenHash);
        Assert.Null(latestRefreshToken.RevokedAt);
        Assert.True(latestRefreshToken.IsActive);
        Assert.True(latestRefreshToken.ExpiresAt > DateTime.UtcNow);

        // 7. Sensitive data exposure assertions
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain(latestRefreshToken.TokenHash, body);
        Assert.DoesNotContain(rawCookieToken, body);
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsGeneric401_AndCreatesNoDatabaseSession()
    {
        // 1. Ensure user exists
        const string email = "e2e.invalid.pass@vaultx.local";
        const string correctPassword = "CorrectPassword123!";
        const string wrongPassword = "WrongPassword999!";

        await RegisterUserViaApiAsync(email, correctPassword);

        // Record existing refresh token count
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.Include(u => u.RefreshTokens).FirstOrDefaultAsync(u => u.Email == email);
            var initialTokenCount = user?.RefreshTokens.Count ?? 0;

            // 2. Submit Login with wrong password
            var loginPayload = new LoginRequest(email, wrongPassword);
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);

            // 3. Verify HTTP 401 Unauthorized
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // 4. Verify generic error message preserving user enumeration protection
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var detail = doc.RootElement.GetProperty("detail").GetString();
            Assert.Equal("Invalid email or password.", detail);

            // 5. Verify no cookie is set
            Assert.False(response.Headers.Contains("Set-Cookie"));

            // 6. Verify no new refresh token was created in the database
            var refreshedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var refreshedUser = await refreshedDb.Users.Include(u => u.RefreshTokens).FirstAsync(u => u.Email == email);
            Assert.Equal(initialTokenCount, refreshedUser.RefreshTokens.Count);
        }
    }

    [Fact]
    public async Task Login_NonExistentEmail_ReturnsIdenticalGeneric401_PreventingUserEnumeration()
    {
        // 1. Submit Login with completely unknown email
        var loginPayload = new LoginRequest("nonexistent.user.e2e@vaultx.local", "SomePassword123!");
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);

        // 2. Verify identical 401 Unauthorized ProblemDetails
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var detail = doc.RootElement.GetProperty("detail").GetString();
        Assert.Equal("Invalid email or password.", detail);

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_EmptyCredentials_Returns400BadRequest_WithValidationErrors()
    {
        // 1. Submit empty email and password
        var loginPayload = new LoginRequest("", "");
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);

        // 2. Verify 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("errors", out var errorsProp));
        Assert.True(errorsProp.TryGetProperty("Email", out _));
        Assert.True(errorsProp.TryGetProperty("Password", out _));
    }
}
