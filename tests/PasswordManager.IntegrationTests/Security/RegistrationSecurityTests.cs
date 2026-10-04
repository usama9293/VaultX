using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Infrastructure.Persistence;
using Xunit;

namespace PasswordManager.IntegrationTests.Security;

public class RegistrationSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public RegistrationSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidationBypass_EmptyEmail_RejectedByBackend()
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            email = "",
            password = "StrongPassword@123",
            confirmPassword = "StrongPassword@123"
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email is required.", body);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("@domain.com")]
    [InlineData("user..name@domain.com")]
    public async Task ValidationBypass_InvalidEmail_RejectedByBackend(string invalidEmail)
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            email = invalidEmail,
            password = "StrongPassword@123",
            confirmPassword = "StrongPassword@123"
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email format is invalid.", body);
    }

    [Theory]
    [InlineData("", "Password is required.")]
    [InlineData("Short1!Aa", "Password must be at least 12 characters long.")]
    [InlineData("alllowercase123!", "Password must contain at least one uppercase letter.")]
    [InlineData("ALLUPPERCASE123!", "Password must contain at least one lowercase letter.")]
    [InlineData("NoDigitsAtAll!@#", "Password must contain at least one digit.")]
    [InlineData("NoSpecialCharacters123", "Password must contain at least one special character.")]
    public async Task ValidationBypass_WeakPassword_RejectedByBackend(string weakPassword, string expectedError)
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            email = "bypass.test@example.com",
            password = weakPassword,
            confirmPassword = weakPassword
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedError, body);
    }

    [Fact]
    public async Task ValidationBypass_PasswordMismatch_RejectedByBackend()
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            email = "mismatch.test@example.com",
            password = "StrongPassword@123",
            confirmPassword = "DifferentPassword@123"
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Passwords do not match.", body);
    }

    [Fact]
    public async Task ValidationBypass_OversizedEmail_RejectedByBackend()
    {
        var oversizedEmail = new string('a', 315) + "@test.com"; // > 320 chars
        var rawJson = JsonSerializer.Serialize(new
        {
            email = oversizedEmail,
            password = "StrongPassword@123",
            confirmPassword = "StrongPassword@123"
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email must not exceed 320 characters.", body);
    }

    [Fact]
    public async Task ValidationBypass_OversizedPassword_RejectedByBackend()
    {
        var oversizedPassword = new string('A', 126) + "1!a"; // 129 chars (>128)
        var rawJson = JsonSerializer.Serialize(new
        {
            email = "oversizedpass.test@example.com",
            password = oversizedPassword,
            confirmPassword = oversizedPassword
        });

        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Password must not exceed 128 characters.", body);
    }

    [Fact]
    public async Task MassAssignment_AttackerControlledFields_IgnoredAndServerControlled()
    {
        var fakeId = Guid.NewGuid();
        var fakeHash = "attacker_controlled_hash_string";
        var ancientDate = "2000-01-01T00:00:00Z";
        var targetEmail = "massassign.test@example.com";

        var maliciousPayload = JsonSerializer.Serialize(new
        {
            email = targetEmail,
            password = "StrongPassword@123",
            confirmPassword = "StrongPassword@123",
            id = fakeId,
            passwordHash = fakeHash,
            createdAt = ancientDate,
            updatedAt = ancientDate,
            isAdmin = true
        });

        var content = new StringContent(maliciousPayload, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var returnedId = doc.RootElement.GetProperty("id").GetGuid();

        // Server generates its own ID, ignoring attacker's ID
        Assert.NotEqual(fakeId, returnedId);

        // Verify in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == targetEmail);

        Assert.NotNull(user);
        Assert.NotEqual(fakeId, user!.Id);

        // Password hash must NOT be the attacker-supplied fake hash
        var hashString = Encoding.UTF8.GetString(user.PasswordHash);
        Assert.NotEqual(fakeHash, hashString);
        Assert.StartsWith("$pbkdf2-sha256$", hashString);

        // Timestamp must NOT be the attacker-supplied year 2000
        Assert.True(user.CreatedAt > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task DuplicateEmail_Sequential_Returns409Conflict()
    {
        var email = "sequential.duplicate@example.com";
        var payload = new RegisterUserRequest(email, "StrongPassword@123", "StrongPassword@123");

        var firstResp = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);

        var secondResp = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Conflict, secondResp.StatusCode);

        var body = await secondResp.Content.ReadAsStringAsync();
        Assert.Contains("A user with this email already exists.", body);
    }

    [Fact]
    public async Task DuplicateEmail_ConcurrentRaceCondition_PreservesDatabaseUniqueness()
    {
        var raceEmail = "racecondition.test@example.com";
        var payload = new RegisterUserRequest(raceEmail, "StrongPassword@123", "StrongPassword@123");

        // Send 5 concurrent requests simultaneously
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _client.PostAsJsonAsync("/api/auth/register", payload))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        var responseStatuses = string.Join(", ", responses.Select(response => (int)response.StatusCode));
        Assert.All(responses, response =>
            Assert.True(
                response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"Every duplicate registration request must return 201 or 409. Received: [{responseStatuses}]"));

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        // Exactly one request must succeed and the others must receive Conflict
        Assert.Equal(1, successCount);
        Assert.Equal(4, conflictCount);

        // Exactly one user must exist in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = await db.Users.Where(u => u.Email == raceEmail).ToListAsync();
        Assert.Single(users);
    }

    [Fact]
    public async Task ResponseExposure_NeverContainsSensitiveFields()
    {
        var email = "response.exposure@example.com";
        var password = "SuperSecretP@ssword999!";
        var payload = new RegisterUserRequest(email, password, password);

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // Plaintext password must never be in response
        Assert.DoesNotContain(password, body);

        // Password hash must never be in response
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$pbkdf2", body);

        // Keys / tokens must never be in response
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jwt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vault", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ErrorDisclosure_MalformedJson_ReturnsSafeProblemDetailsWithoutInternalLeaks()
    {
        var malformedContent = new StringContent("{ not valid json: true }", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/register", malformedContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // Must not expose stack traces or internal server file paths
        Assert.DoesNotContain(".cs:line", body);
        Assert.DoesNotContain("Stack trace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ErrorDisclosure_DuplicateEmail_DoesNotExposeDatabaseInternalDetails()
    {
        var email = "error.leak.test@example.com";
        var payload = new RegisterUserRequest(email, "StrongPassword@123", "StrongPassword@123");

        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // Must not expose SQL syntax, table names, or internal exception class names
        Assert.DoesNotContain("SqliteException", body);
        Assert.DoesNotContain("PostgresException", body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain("INSERT INTO", body);
        Assert.DoesNotContain("IX_Users_Email", body);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task HttpMethodHandling_UnsupportedMethods_ReturnsMethodNotAllowed(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/auth/register");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Cors_AllowedDevelopmentOrigin_GrantedAccessHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = JsonContent.Create(new RegisterUserRequest("cors.test@example.com", "StrongPassword@123", "StrongPassword@123"));

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_ArbitraryDisallowedOrigin_NotGrantedAccessHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        request.Headers.Add("Origin", "http://evil-attacker.com");
        request.Content = JsonContent.Create(new RegisterUserRequest("cors.bad@example.com", "StrongPassword@123", "StrongPassword@123"));

        var response = await _client.SendAsync(request);

        if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins))
        {
            Assert.DoesNotContain("http://evil-attacker.com", origins);
            Assert.DoesNotContain("*", origins);
        }
    }
}
