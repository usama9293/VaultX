using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Infrastructure.Persistence;
using Xunit;

namespace PasswordManager.IntegrationTests.Controllers;

public class AuthControllerLoginIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthControllerLoginIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task RegisterTestUserAsync(string email, string password)
    {
        var registerRequest = new RegisterUserRequest(email, password, password);
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithAccessTokenAndHttpOnlyCookie()
    {
        // Arrange
        const string email = "valid.login@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterTestUserAsync(email, password);

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("accessToken", out var accessTokenProp));
        Assert.False(string.IsNullOrWhiteSpace(accessTokenProp.GetString()));

        Assert.True(root.TryGetProperty("expiresAt", out var expiresAtProp));
        Assert.True(expiresAtProp.GetDateTime() > DateTime.UtcNow);

        // Verify HttpOnly cookie header
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var setCookieHeader = response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(setCookieHeader);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Response_ContainsOnlySafeMetadata()
    {
        // Arrange
        const string email = "safe.metadata@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterTestUserAsync(email, password);

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert sensitive information is absent from response body
        Assert.DoesNotContain(password, responseBody);
        Assert.DoesNotContain("passwordHash", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$pbkdf2", responseBody);
        Assert.DoesNotContain("refreshToken", responseBody, StringComparison.OrdinalIgnoreCase); // raw refresh token not in JSON
        Assert.DoesNotContain("tokenHash", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Database_PersistsHashedRefreshTokenNotRawToken()
    {
        // Arrange
        const string email = "db.persistence@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterTestUserAsync(email, password);

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var rawToken = setCookie.Split(';')[0]["refreshToken=".Length..];

        // Assert database record
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.Include(u => u.RefreshTokens).FirstAsync(u => u.Email == email);

        Assert.NotEmpty(user.RefreshTokens);
        var tokenRecord = user.RefreshTokens.Last();

        Assert.NotEqual(rawToken, tokenRecord.TokenHash); // Token is hashed
        Assert.Equal(64, tokenRecord.TokenHash.Length); // SHA-256 hex string
        Assert.True(tokenRecord.IsActive);
        Assert.False(tokenRecord.IsExpired);
        Assert.False(tokenRecord.IsRevoked);
    }

    [Fact]
    public async Task Login_MultipleSessions_CreatesMultipleActiveTokens()
    {
        // Arrange
        const string email = "multisession@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterTestUserAsync(email, password);

        var loginRequest = new LoginRequest(email, password);

        // Act - login twice (e.g. desktop and mobile)
        var response1 = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var response2 = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Assert both tokens are retained in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.Include(u => u.RefreshTokens).FirstAsync(u => u.Email == email);

        Assert.True(user.RefreshTokens.Count >= 2);
        Assert.All(user.RefreshTokens, t => Assert.True(t.IsActive));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401UnauthorizedWithGenericMessage()
    {
        // Arrange
        const string email = "wrongpass@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterTestUserAsync(email, password);

        var loginRequest = new LoginRequest(email, "IncorrectPassword999!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        Assert.Equal("Unauthorized", root.GetProperty("title").GetString());
        Assert.Equal("Invalid email or password.", root.GetProperty("detail").GetString());

        // No Set-Cookie header on failure
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_NonexistentEmail_Returns401UnauthorizedWithGenericMessage()
    {
        // Arrange
        var loginRequest = new LoginRequest("nonexistent.user@vaultx.local", "VaultX@SecurePass2026!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        Assert.Equal("Unauthorized", root.GetProperty("title").GetString());
        Assert.Equal("Invalid email or password.", root.GetProperty("detail").GetString()); // Same message as wrong password

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_CaseInsensitiveEmail_Succeeds()
    {
        // Arrange
        const string email = "case.sensitivity@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await RegisterTestUserAsync(email, password);

        var loginRequest = new LoginRequest("CASE.SENSITIVITY@VAULTX.LOCAL", password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("", "VaultX@SecurePass2026!", "Email is required.")]
    [InlineData("not-an-email", "VaultX@SecurePass2026!", "Email format is invalid.")]
    [InlineData("user@vaultx.local", "", "Password is required.")]
    public async Task Login_ValidationBypass_Returns400BadRequest(string email, string password, string expectedError)
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            email,
            password
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedError, body);
    }
}
