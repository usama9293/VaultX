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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSettings.SecretKey));
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

    [Theory]
    [InlineData("{\"password\": \"ValidPassword123!\"}", "Email")]
    [InlineData("{\"email\": \"valid@vaultx.local\"}", "Password")]
    [InlineData("{}", "Email")]
    public async Task Login_MissingEmailOrPasswordProperty_Returns400BadRequest(string jsonPayload, string expectedField)
    {
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedField, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_MassAssignment_UnexpectedJsonFields_IgnoredAndServerControlled()
    {
        const string email = "massassign.login@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, password, password));

        var payload = JsonSerializer.Serialize(new
        {
            email,
            password,
            isAdmin = true,
            roles = new[] { "Admin", "SuperUser" },
            id = Guid.NewGuid(),
            passwordHash = "fake-hash",
            __proto__ = new { polluted = true }
        });

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("accessToken", out var tokenProp));
        Assert.True(root.TryGetProperty("expiresAt", out _));
        Assert.False(root.TryGetProperty("isAdmin", out _));
        Assert.False(root.TryGetProperty("roles", out _));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenProp.GetString());
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("role", StringComparison.OrdinalIgnoreCase) || c.Value.Contains("Admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_UserEnumeration_ExistingVsNonexistentUser_IndistinguishableResponses()
    {
        const string existingEmail = "user.enum.exists@vaultx.local";
        const string validPassword = "VaultX@SecurePass2026!";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(existingEmail, validPassword, validPassword));

        const string wrongPassword = "WrongPassword999!";
        const string nonexistentEmail = "user.enum.nonexistent@vaultx.local";

        var responseExisting = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(existingEmail, wrongPassword));
        var responseNonexistent = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(nonexistentEmail, wrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, responseExisting.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, responseNonexistent.StatusCode);

        Assert.Equal(responseExisting.Content.Headers.ContentType?.MediaType, responseNonexistent.Content.Headers.ContentType?.MediaType);

        Assert.False(responseExisting.Headers.Contains("Set-Cookie"));
        Assert.False(responseNonexistent.Headers.Contains("Set-Cookie"));

        var bodyExisting = await responseExisting.Content.ReadAsStringAsync();
        var bodyNonexistent = await responseNonexistent.Content.ReadAsStringAsync();

        using var docExisting = JsonDocument.Parse(bodyExisting);
        using var docNonexistent = JsonDocument.Parse(bodyNonexistent);

        Assert.Equal(docExisting.RootElement.GetProperty("title").GetString(), docNonexistent.RootElement.GetProperty("title").GetString());
        Assert.Equal(docExisting.RootElement.GetProperty("detail").GetString(), docNonexistent.RootElement.GetProperty("detail").GetString());
        Assert.Equal("Invalid email or password.", docExisting.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_BoundaryLength_OversizedEmail_Returns400BadRequest()
    {
        var oversizedEmail = new string('a', 315) + "@test.com"; // 324 chars (>320)
        var content = JsonContent.Create(new LoginRequest(oversizedEmail, "AnyPassword123!"));

        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email must not exceed 320 characters.", body);
    }

    [Fact]
    public async Task Login_BoundaryLength_VeryLongPassword_HandlesSafelyWithout500OrCrash()
    {
        var veryLongPassword = new string('A', 10000);
        var content = JsonContent.Create(new LoginRequest("longpass.test@vaultx.local", veryLongPassword));

        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".cs:line", body);
    }

    [Fact]
    public async Task Login_UnicodeAndSpecialCharacters_AuthenticatesSuccessfully()
    {
        const string email = "unicode.auth@vaultx.local";
        const string complexPassword = "🔒P@$$w0rd_With_Üñîçødé_&_Emojis!🔑";

        var registerResp = await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, complexPassword, complexPassword));
        Assert.True(registerResp.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, complexPassword));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var body = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("accessToken", out var tokenProp));
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()));
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("' UNION SELECT * FROM \"Users\"--")]
    [InlineData("admin'--")]
    [InlineData("test@vaultx.local'; DROP TABLE \"Users\";--")]
    public async Task Login_SqlInjectionInEmail_SafelyRejectedWithout500OrDbLeak(string sqlPayload)
    {
        var content = JsonContent.Create(new LoginRequest(sqlPayload, "SomePassword123!"));
        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("syntax error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body);
    }

    [Fact]
    public async Task Login_SqlInjectionInPassword_SafelyHandledWithoutDatabaseExecution()
    {
        const string email = "sql.pass.test@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, password, password));

        const string sqlPassword = "' OR '1'='1'; DROP TABLE \"Users\";--";
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, sqlPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password.", body);
        Assert.DoesNotContain("syntax error", body, StringComparison.OrdinalIgnoreCase);

        var retryResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
    }

    [Fact]
    public async Task Login_AccessToken_ContainsOnlyIntendedClaims_NoSensitiveData()
    {
        const string email = "jwt.claims.audit@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, password, password));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("accessToken").GetString()!;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub || c.Type == ClaimTypes.NameIdentifier));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email || c.Type == ClaimTypes.Email));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti));

        var prohibitedNames = new[] { "password", "passwordHash", "tokenHash", "refreshToken", "rawRefreshToken", "role", "isAdmin", "secret" };
        foreach (var claim in jwt.Claims)
        {
            Assert.DoesNotContain(claim.Type, prohibitedNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("$pbkdf2", claim.Value);
            Assert.DoesNotContain(password, claim.Value);
        }
    }

    [Fact]
    public async Task JwtMiddleware_NoneAlgorithm_Returns401Unauthorized()
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"00000000-0000-0000-0000-000000000001\",\"iss\":\"VaultX.API\",\"aud\":\"VaultX.Client\",\"exp\":2524608000}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var noneToken = $"{header}.{payload}.";

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noneToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JwtMiddleware_TamperedPayload_Returns401Unauthorized()
    {
        var validToken = await GetValidAccessTokenAsync();
        var parts = validToken.Split('.');
        Assert.Equal(3, parts.Length);

        var tamperedPayloadJson = "{\"sub\":\"00000000-0000-0000-0000-000000000999\",\"iss\":\"VaultX.API\",\"aud\":\"VaultX.Client\"}";
        var tamperedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(tamperedPayloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var forgedToken = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("header.only")]
    [InlineData("a.b.c.d.e")]
    [InlineData("???malformed???")]
    public async Task JwtMiddleware_MalformedBearerToken_Returns401Unauthorized(string malformedToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", malformedToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Response_NeverExposesPasswordOrHashesInFailureOrSuccess()
    {
        const string email = "leak.test@vaultx.local";
        const string password = "VaultX@SecurePass2026!";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(email, password, password));

        var failResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword123!"));
        var failBody = await failResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("WrongPassword123!", failBody);
        Assert.DoesNotContain(password, failBody);
        Assert.DoesNotContain("$pbkdf2", failBody);
        Assert.DoesNotContain("passwordHash", failBody, StringComparison.OrdinalIgnoreCase);

        var successResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var successBody = await successResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, successBody);
        Assert.DoesNotContain("$pbkdf2", successBody);
        Assert.DoesNotContain("passwordHash", successBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", successBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_UnsupportedContentType_Returns415UnsupportedMediaType()
    {
        var content = new StringContent("email=user%40vaultx.local&password=Pass", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
