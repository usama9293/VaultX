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
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;
using VaultEntity = PasswordManager.Domain.Entities.Vault;

namespace PasswordManager.IntegrationTests.Security;

public sealed class VaultEntryApiSecurityTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VaultEntryApiSecurityTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task EntryCrud_IsOwnerScopedAndReturnsOnlyAllowlistedMetadata()
    {
        var owner = await CreateAuthenticatedUserAsync();
        var vaultId = await InitializeVaultAsync(owner.AccessToken);
        var body = new
        {
            title = "  Example account ",
            websiteUrl = "https://example.test/path",
            username = "user@example.test",
            notes = "<script>alert(1)</script>",
            userId = Guid.NewGuid(),
            vaultId = Guid.NewGuid(),
            password = "must-not-be-accepted",
            encryptedPassword = Convert.ToBase64String([1, 2, 3]),
            passwordNonce = Convert.ToBase64String([4, 5, 6]),
            passwordAuthenticationTag = Convert.ToBase64String([7, 8, 9])
        };

        using var createResponse = await SendJsonAsync(
            HttpMethod.Post, "/api/vault/entries", owner.AccessToken, body);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var created = createJson.RootElement;
        Assert.Equal(
            new[] { "createdAt", "id", "notes", "title", "updatedAt", "username", "websiteUrl" },
            created.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("Example account", created.GetProperty("title").GetString());
        Assert.Equal("user@example.test", created.GetProperty("username").GetString());
        Assert.DoesNotContain("password", created.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vaultId", created.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var entryId = created.GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.VaultEntries.SingleAsync(entry => entry.Id == entryId);
            var state = db.Entry(stored);
            Assert.Null(state.Property<byte[]?>("EncryptedPassword").CurrentValue);
            Assert.Null(state.Property<byte[]?>("PasswordNonce").CurrentValue);
            Assert.Null(state.Property<byte[]?>("PasswordAuthenticationTag").CurrentValue);
        }

        using var listResponse = await SendAsync(HttpMethod.Get, "/api/vault/entries", owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var page = listJson.RootElement;
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var listItem = page.GetProperty("items")[0];
        Assert.Equal(
            new[] { "createdAt", "id", "title", "updatedAt", "username", "websiteUrl" },
            listItem.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("notes", listItem.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var detailResponse = await SendAsync(
            HttpMethod.Get, $"/api/vault/entries/{entryId}", owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("<script>alert(1)</script>", detailJson.RootElement.GetProperty("notes").GetString());
        Assert.DoesNotContain("encryptedPassword", detailJson.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var notesSearch = await SendAsync(
            HttpMethod.Get,
            "/api/vault/entries?q=alert%281%29",
            owner.AccessToken);
        using var notesSearchJson = JsonDocument.Parse(await notesSearch.Content.ReadAsStringAsync());
        Assert.Empty(notesSearchJson.RootElement.GetProperty("items").EnumerateArray());

        using var updateResponse = await SendJsonAsync(
            HttpMethod.Put,
            $"/api/vault/entries/{entryId}",
            owner.AccessToken,
            new
            {
                title = "Updated account",
                websiteUrl = (string?)null,
                username = "updated@example.test",
                notes = (string?)null,
                userId = Guid.NewGuid(),
                vaultId = Guid.NewGuid(),
                password = "also-ignored"
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updateJson = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("Updated account", updateJson.RootElement.GetProperty("title").GetString());
        Assert.Null(updateJson.RootElement.GetProperty("websiteUrl").GetString());
        Assert.Equal(vaultId, await GetEntryVaultIdAsync(entryId));

        using var deleteResponse = await SendAsync(
            HttpMethod.Delete, $"/api/vault/entries/{entryId}", owner.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        using var missingResponse = await SendAsync(
            HttpMethod.Get, $"/api/vault/entries/{entryId}", owner.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task ForeignAndMissingEntriesHaveIndistinguishableResponsesForEveryOperation()
    {
        var userA = await CreateAuthenticatedUserAsync();
        var userB = await CreateAuthenticatedUserAsync();
        await InitializeVaultAsync(userA.AccessToken);
        await InitializeVaultAsync(userB.AccessToken);
        using var createResponse = await SendJsonAsync(
            HttpMethod.Post,
            "/api/vault/entries",
            userA.AccessToken,
            new { title = "Private label", username = "private-user" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createdJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var entryId = createdJson.RootElement.GetProperty("id").GetGuid();
        var missingId = Guid.NewGuid();

        foreach (var operation in new[]
        {
            (HttpMethod.Get, $"/api/vault/entries/{entryId}"),
            (HttpMethod.Put, $"/api/vault/entries/{entryId}"),
            (HttpMethod.Delete, $"/api/vault/entries/{entryId}")
        })
        {
            using var foreign = operation.Item1 == HttpMethod.Put
                ? await SendJsonAsync(operation.Item1, operation.Item2, userB.AccessToken,
                    new { title = "attempt", username = "attempt" })
                : await SendAsync(operation.Item1, operation.Item2, userB.AccessToken);
            using var missing = operation.Item1 == HttpMethod.Put
                ? await SendJsonAsync(operation.Item1, operation.Item2.Replace(entryId.ToString(), missingId.ToString()), userB.AccessToken,
                    new { title = "attempt", username = "attempt" })
                : await SendAsync(operation.Item1, operation.Item2.Replace(entryId.ToString(), missingId.ToString()), userB.AccessToken);

            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.Equal(foreign.StatusCode, missing.StatusCode);
            Assert.Equal(
                await foreign.Content.ReadAsStringAsync(),
                await missing.Content.ReadAsStringAsync());
        }

        using var userBList = await SendAsync(HttpMethod.Get, "/api/vault/entries", userB.AccessToken);
        using var userBListJson = JsonDocument.Parse(await userBList.Content.ReadAsStringAsync());
        Assert.Empty(userBListJson.RootElement.GetProperty("items").EnumerateArray());

        using var userBSearch = await SendAsync(
            HttpMethod.Get, "/api/vault/entries?q=Private", userB.AccessToken);
        using var userBSearchJson = JsonDocument.Parse(await userBSearch.Content.ReadAsStringAsync());
        Assert.Empty(userBSearchJson.RootElement.GetProperty("items").EnumerateArray());

        using var spoofedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/vault/entries?vaultId={Guid.NewGuid()}&userId={userA.UserId}");
        spoofedRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", userB.AccessToken);
        spoofedRequest.Headers.Add("X-UserId", userA.UserId.ToString());
        spoofedRequest.Headers.Add("X-VaultId", Guid.NewGuid().ToString());
        using var spoofedResponse = await _client.SendAsync(spoofedRequest);
        using var spoofedJson = JsonDocument.Parse(await spoofedResponse.Content.ReadAsStringAsync());
        Assert.Empty(spoofedJson.RootElement.GetProperty("items").EnumerateArray());

        using var userAGet = await SendAsync(
            HttpMethod.Get, $"/api/vault/entries/{entryId}", userA.AccessToken);
        Assert.Equal(HttpStatusCode.OK, userAGet.StatusCode);
    }

    [Fact]
    public async Task AllEntryRoutesRejectAnonymousMalformedAndExpiredTokens()
    {
        var user = await CreateAuthenticatedUserAsync();
        await InitializeVaultAsync(user.AccessToken);
        var requests = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, "/api/vault/entries", new { title = "Title", username = "User" }),
            (HttpMethod.Get, "/api/vault/entries", null),
            (HttpMethod.Get, $"/api/vault/entries/{Guid.NewGuid()}", null),
            (HttpMethod.Put, $"/api/vault/entries/{Guid.NewGuid()}", new { title = "Title", username = "User" }),
            (HttpMethod.Delete, $"/api/vault/entries/{Guid.NewGuid()}", null)
        };

        foreach (var request in requests)
        {
            using var anonymous = request.Body is null
                ? await SendAsync(request.Method, request.Path, accessToken: null)
                : await SendJsonAsync(request.Method, request.Path, accessToken: null, request.Body);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            foreach (var token in new[]
            {
                "not-a-jwt",
                CreateToken(Guid.NewGuid().ToString(), DateTime.UtcNow.AddMinutes(-5)),
                CreateToken("not-a-guid", DateTime.UtcNow.AddMinutes(5))
            })
            {
                using var response = request.Body is null
                    ? await SendAsync(request.Method, request.Path, token)
                    : await SendJsonAsync(request.Method, request.Path, token, request.Body);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task MissingVaultAndInvalidRequestsDoNotCreateOrEnumerateResources()
    {
        var user = await CreateAuthenticatedUserAsync();
        using var missingVault = await SendAsync(HttpMethod.Get, "/api/vault/entries", user.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, missingVault.StatusCode);
        using var missingCreate = await SendJsonAsync(
            HttpMethod.Post,
            "/api/vault/entries",
            user.AccessToken,
            new { title = "Title", username = "User" });
        Assert.Equal(HttpStatusCode.NotFound, missingCreate.StatusCode);
        using (var beforeInitialization = _factory.Services.CreateScope())
        {
            var beforeDb = beforeInitialization.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await beforeDb.Vaults.AnyAsync(vault => vault.UserId == user.UserId));
        }

        using var malformedId = await SendAsync(
            HttpMethod.Get, "/api/vault/entries/not-a-guid", user.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, malformedId.StatusCode);
        await InitializeVaultAsync(user.AccessToken);
        using var invalidSearch = await SendAsync(
            HttpMethod.Get, $"/api/vault/entries?q={new string('a', 129)}", user.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSearch.StatusCode);
        using var invalidPagination = await SendAsync(
            HttpMethod.Get, "/api/vault/entries?page=0", user.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPagination.StatusCode);
        using var invalidUrl = await SendJsonAsync(
            HttpMethod.Post,
            "/api/vault/entries",
            user.AccessToken,
            new { title = "Title", username = "User", websiteUrl = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUrl.StatusCode);
        Assert.DoesNotContain("javascript", await invalidUrl.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var oversizedRequest = await SendJsonAsync(
            HttpMethod.Post,
            "/api/vault/entries",
            user.AccessToken,
            new { title = "Title", username = "User", notes = new string('a', 33_000) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedRequest.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.VaultEntries.ToListAsync());
    }

    [Fact]
    public async Task EntrySearchTreatsWildcardsLiterallyAndPaginatesStably()
    {
        var user = await CreateAuthenticatedUserAsync();
        await InitializeVaultAsync(user.AccessToken);
        foreach (var title in new[] { "100% Secure", "100X Secure", "Under_score", "Another" })
        {
            using var response = await SendJsonAsync(
                HttpMethod.Post,
                "/api/vault/entries",
                user.AccessToken,
                new { title, username = "search-user" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var percentSearch = await SendAsync(
            HttpMethod.Get, "/api/vault/entries?q=%25&pageSize=10", user.AccessToken);
        using var percentJson = JsonDocument.Parse(await percentSearch.Content.ReadAsStringAsync());
        Assert.Equal(1, percentJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("100% Secure", percentJson.RootElement.GetProperty("items")[0].GetProperty("title").GetString());

        using var underscoreSearch = await SendAsync(
            HttpMethod.Get, "/api/vault/entries?q=_", user.AccessToken);
        using var underscoreJson = JsonDocument.Parse(await underscoreSearch.Content.ReadAsStringAsync());
        Assert.Equal(1, underscoreJson.RootElement.GetProperty("items").GetArrayLength());

        using var firstPage = await SendAsync(
            HttpMethod.Get, "/api/vault/entries?page=1&pageSize=2", user.AccessToken);
        using var firstJson = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync());
        Assert.True(firstJson.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, firstJson.RootElement.GetProperty("items").GetArrayLength());
        using var lastPage = await SendAsync(
            HttpMethod.Get, "/api/vault/entries?page=2&pageSize=2", user.AccessToken);
        using var lastJson = JsonDocument.Parse(await lastPage.Content.ReadAsStringAsync());
        Assert.False(lastJson.RootElement.GetProperty("hasMore").GetBoolean());
        var orderedItems = firstJson.RootElement.GetProperty("items").EnumerateArray()
            .ToArray()
            .Concat(lastJson.RootElement.GetProperty("items").EnumerateArray())
            .ToArray();
        for (var index = 1; index < orderedItems.Length; index++)
        {
            var previousTime = orderedItems[index - 1].GetProperty("createdAt").GetDateTimeOffset();
            var currentTime = orderedItems[index].GetProperty("createdAt").GetDateTimeOffset();
            Assert.True(previousTime >= currentTime);
            if (previousTime == currentTime)
            {
                Assert.True(
                    orderedItems[index - 1].GetProperty("id").GetGuid()
                    .CompareTo(orderedItems[index].GetProperty("id").GetGuid()) < 0);
            }
        }
    }

    private async Task<Guid> InitializeVaultAsync(string accessToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/vault", accessToken);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(Guid UserId, string AccessToken)> CreateAuthenticatedUserAsync()
    {
        var email = $"entries-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@VaultPhase4_2026!";
        using var register = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(registered);

        using var login = await _client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return (registered.Id, token!);
    }

    private async Task<Guid> GetEntryVaultIdAsync(Guid entryId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.VaultEntries
            .Where(entry => entry.Id == entryId)
            .Select(entry => entry.VaultId)
            .SingleAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? accessToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string path,
        string? accessToken,
        object body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        return await _client.SendAsync(request);
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
