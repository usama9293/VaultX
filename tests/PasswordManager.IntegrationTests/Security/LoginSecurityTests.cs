using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using PasswordManager.Application.DTOs.Authentication;
using Xunit;

namespace PasswordManager.IntegrationTests.Security;

public class LoginSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public LoginSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetValidAccessTokenAsync()
    {
        const string email = "jwt.security.test@vaultx.local";
        const string password = "VaultX@SecurePass2026!";

        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, password, password));

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var body = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task JwtMiddleware_ValidToken_AllowsAccessToProtectedEndpoint()
    {
        var token = await GetValidAccessTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task JwtMiddleware_NoToken_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/test-auth/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JwtMiddleware_TamperedSignature_Returns401Unauthorized()
    {
        // Token signed with an attacker's different key
        var attackerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("AttackerForgedSecretKeyMustBe32CharsLong!"));
        var tokenHandler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "VaultX.API",
            Audience = "VaultX.Client",
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(attackerKey, SecurityAlgorithms.HmacSha256)
        };

        var forgedToken = tokenHandler.CreateToken(descriptor);
        var forgedTokenString = tokenHandler.WriteToken(forgedToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedTokenString);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JwtMiddleware_ExpiredToken_Returns401Unauthorized()
    {
        const string validSecret = "VaultX-Development-SecretKey-ChangeInProduction-Minimum256Bits!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(validSecret));
        var tokenHandler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "VaultX.API",
            Audience = "VaultX.Client",
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }),
            NotBefore = DateTime.UtcNow.AddMinutes(-15),
            Expires = DateTime.UtcNow.AddMinutes(-5), // Expired 5 minutes ago
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var expiredToken = tokenHandler.CreateToken(descriptor);
        var expiredTokenString = tokenHandler.WriteToken(expiredToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredTokenString);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_CookieSecurity_HasHttpOnlyAndScopedPath()
    {
        const string email = "cookie.security.test@vaultx.local";
        const string password = "VaultX@SecurePass2026!";

        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, password, password));
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));

        var cookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Cors_AllowsConfiguredDevelopmentOriginWithCredentials()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = JsonContent.Create(new LoginRequest("cors.test@vaultx.local", "SomePassword123!"));

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.True(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Contains("true", response.Headers.GetValues("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Login_Cors_RejectsArbitraryOrigin()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add("Origin", "http://untrusted-attacker.com");
        request.Content = JsonContent.Create(new LoginRequest("cors.bad@vaultx.local", "SomePassword123!"));

        var response = await _client.SendAsync(request);

        if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins))
        {
            Assert.DoesNotContain("http://untrusted-attacker.com", origins);
            Assert.DoesNotContain("*", origins);
        }
    }

    [Fact]
    public async Task Login_ErrorDisclosure_MalformedJson_DoesNotExposeStackTraceOrServerPaths()
    {
        var content = new StringContent("{ not valid json: true }", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(".cs:line", body);
        Assert.DoesNotContain("Stack trace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Login_UnsupportedHttpMethods_Returns405MethodNotAllowed(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/auth/login");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
