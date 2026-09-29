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

public class AuthControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidPayload_Returns201CreatedAndSafeResponse()
    {
        // Arrange
        var request = new RegisterUserRequest(
            "valid.user@example.com",
            "SuperStrongP@ssw0rd!123",
            "SuperStrongP@ssw0rd!123");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        // Observable security checks:
        Assert.True(root.TryGetProperty("id", out var idProp));
        Assert.NotEqual(Guid.Empty, idProp.GetGuid());

        Assert.True(root.TryGetProperty("email", out var emailProp));
        Assert.Equal("valid.user@example.com", emailProp.GetString());

        Assert.True(root.TryGetProperty("createdAt", out _));
        Assert.True(root.TryGetProperty("updatedAt", out _));

        // Must NEVER expose password, passwordHash, or cryptographic keys
        Assert.False(root.TryGetProperty("password", out _));
        Assert.False(root.TryGetProperty("passwordHash", out _));
        Assert.False(root.TryGetProperty("PasswordHash", out _));
        Assert.DoesNotContain("SuperStrongP@ssw0rd!123", body);

        // Verify persistence in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedUser = await db.Users.FirstOrDefaultAsync(u => u.Email == "valid.user@example.com");

        Assert.NotNull(persistedUser);
        Assert.NotEmpty(persistedUser!.PasswordHash);

        // Verify password is NOT stored as plaintext
        var rawPasswordBytes = Encoding.UTF8.GetBytes(request.Password);
        Assert.False(persistedUser.PasswordHash.SequenceEqual(rawPasswordBytes));
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409ConflictWithSafeProblemDetails()
    {
        // Arrange
        var request = new RegisterUserRequest(
            "duplicate.test@example.com",
            "StrongPassword#123",
            "StrongPassword#123");

        // First registration
        var firstResponse = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Act - Attempt duplicate registration
        var duplicateResponse = await _client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var body = await duplicateResponse.Content.ReadAsStringAsync();
        Assert.Contains("A user with this email already exists.", body);

        // Must not expose internal database details (e.g. PostgresException, SqliteException, or SQL table/index statements)
        Assert.DoesNotContain("SqliteException", body);
        Assert.DoesNotContain("PostgresException", body);
        Assert.DoesNotContain("Npgsql", body);
    }

    [Theory]
    [InlineData("", "StrongPassword#123", "StrongPassword#123", "Email is required.")]
    [InlineData("notanemail", "StrongPassword#123", "StrongPassword#123", "Email format is invalid.")]
    [InlineData("test@example.com", "", "", "Password is required.")]
    [InlineData("test@example.com", "weak", "weak", "Password must be at least 12 characters long.")]
    [InlineData("test@example.com", "alllowercase123!", "alllowercase123!", "Password must contain at least one uppercase letter.")]
    [InlineData("test@example.com", "StrongPassword#123", "MismatchPassword#123", "Passwords do not match.")]
    public async Task Register_InvalidPayload_Returns400BadRequestWithValidationErrors(
        string email, string password, string confirmPassword, string expectedError)
    {
        // Arrange
        var request = new RegisterUserRequest(email, password, confirmPassword);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedError, body);
        if (!string.IsNullOrEmpty(password))
        {
            Assert.DoesNotContain(password, body);
        }
    }
}
