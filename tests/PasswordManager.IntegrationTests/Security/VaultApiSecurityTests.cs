using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;
using VaultEntity = PasswordManager.Domain.Entities.Vault;

namespace PasswordManager.IntegrationTests.Security;

public sealed class VaultApiSecurityTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VaultApiSecurityTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task VaultEndpoints_AnonymousMalformedAndExpiredTokensAreUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsync("/api/vault", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/vault")).StatusCode);

        using var malformedRequest = CreateRequest(HttpMethod.Get, "/api/vault", "not-a-valid-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(malformedRequest)).StatusCode);

        using var expiredRequest = CreateRequest(HttpMethod.Get, "/api/vault", CreateToken(
            Guid.NewGuid().ToString(),
            DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(expiredRequest)).StatusCode);
    }

    [Fact]
    public async Task VaultLifecycle_IsOwnerScopedIdempotentAndReturnsOnlyApprovedFields()
    {
        var owner = await CreateAuthenticatedUserAsync();

        using var missingResponse = await SendAsync(HttpMethod.Get, "/api/vault", owner.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var createResponse = await SendAsync(HttpMethod.Post, "/api/vault", owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.True(createResponse.Headers.Location is not null);

        var createdJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var created = createdJson.RootElement;
        Assert.Equal(new[] { "createdAt", "id", "updatedAt" },
            created.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var vaultId = created.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.String, created.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.String, created.GetProperty("updatedAt").ValueKind);
        Assert.DoesNotContain("UserId", created.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Key", created.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Entry", created.GetRawText(), StringComparison.OrdinalIgnoreCase);
        createdJson.Dispose();

        using var getResponse = await SendAsync(
            HttpMethod.Get,
            $"/api/vault?userId={Guid.NewGuid()}",
            owner.AccessToken,
            userIdHeader: Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal(vaultId, getJson.RootElement.GetProperty("id").GetGuid());

        using var idempotentResponse = await SendAsync(HttpMethod.Post, "/api/vault", owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, idempotentResponse.StatusCode);
        using var idempotentJson = JsonDocument.Parse(await idempotentResponse.Content.ReadAsStringAsync());
        Assert.Equal(vaultId, idempotentJson.RootElement.GetProperty("id").GetGuid());

        using var postWithSpoofedBody = new HttpRequestMessage(HttpMethod.Post, "/api/vault")
        {
            Content = JsonContent.Create(new { userId = Guid.NewGuid(), vaultId = Guid.NewGuid() })
        };
        postWithSpoofedBody.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        postWithSpoofedBody.Headers.Add("X-UserId", Guid.NewGuid().ToString());
        using var spoofResponse = await _client.SendAsync(postWithSpoofedBody);
        Assert.Equal(HttpStatusCode.OK, spoofResponse.StatusCode);
        using var spoofJson = JsonDocument.Parse(await spoofResponse.Content.ReadAsStringAsync());
        Assert.Equal(vaultId, spoofJson.RootElement.GetProperty("id").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Vaults.CountAsync(vault => vault.UserId == owner.UserId));
        Assert.Equal(owner.UserId, await db.Vaults
            .Where(vault => vault.Id == vaultId)
            .Select(vault => vault.UserId)
            .SingleAsync());
    }

    [Fact]
    public async Task VaultEndpoints_CannotAccessAnotherUsersVaultAndMalformedIdentityIsUnauthorized()
    {
        var userA = await CreateAuthenticatedUserAsync();
        var userB = await CreateAuthenticatedUserAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(HttpMethod.Post, "/api/vault", userA.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(HttpMethod.Post, "/api/vault", userB.AccessToken)).StatusCode);

        using var responseA = await SendAsync(HttpMethod.Get, "/api/vault", userA.AccessToken);
        using var responseB = await SendAsync(HttpMethod.Get, "/api/vault", userB.AccessToken);
        using var jsonA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        using var jsonB = JsonDocument.Parse(await responseB.Content.ReadAsStringAsync());
        Assert.NotEqual(jsonA.RootElement.GetProperty("id").GetGuid(), jsonB.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

        using var malformedIdentity = CreateRequest(
            HttpMethod.Get,
            "/api/vault",
            CreateToken("not-a-guid", DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(malformedIdentity)).StatusCode);
    }

    [Fact]
    public async Task VaultDatabase_EnforcesUniqueOwnerAndUserForeignKey()
    {
        var owner = await CreateAuthenticatedUserAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Vaults.Add(new VaultEntity(owner.UserId));
        await db.SaveChangesAsync();

        db.Vaults.Add(new VaultEntity(owner.UserId));
        var duplicateException = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_Vaults_UserId", Assert.IsType<PostgresException>(duplicateException.InnerException).ConstraintName);

        db.ChangeTracker.Clear();
        db.Vaults.Add(new VaultEntity(Guid.NewGuid()));
        var foreignKeyException = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("FK_Vaults_Users_UserId",
            Assert.IsType<PostgresException>(foreignKeyException.InnerException).ConstraintName,
            StringComparison.Ordinal);
    }

    private async Task<(Guid UserId, string AccessToken)> CreateAuthenticatedUserAsync()
    {
        var email = $"vault-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@VaultPhase3_2026!";
        using var register = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(registered);

        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return (registered.Id, token!);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string accessToken,
        string? userIdHeader = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (userIdHeader is not null)
        {
            request.Headers.Add("X-UserId", userIdHeader);
        }

        return await _client.SendAsync(request);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string CreateToken(string subject, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSettings.SecretKey));
        var token = new JwtSecurityToken(
            issuer: "VaultX.API",
            audience: "VaultX.Client",
            claims: [new Claim(ClaimTypes.NameIdentifier, subject)],
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
