using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Integration;

public class RefreshTokenIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public RefreshTokenIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task Refresh_RotatesCookieAndReplayRevokesTheEntireFamily()
    {
        var email = $"refresh-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";
        await RegisterAsync(email, password);
        var originalToken = await LoginAndGetRefreshTokenAsync(email, password);

        var refreshResponse = await PostRefreshAsync(originalToken);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var responseBody = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(responseBody);
        Assert.False(string.IsNullOrWhiteSpace(responseBody.AccessToken));
        var replacementToken = GetRefreshTokenCookie(refreshResponse);
        Assert.NotEqual(originalToken, replacementToken);
        var responseJson = await refreshResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(originalToken, responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(replacementToken, responseJson, StringComparison.Ordinal);
        var replacementCookie = refreshResponse.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.Contains("path=/api/auth", replacementCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", replacementCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", replacementCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", replacementCookie, StringComparison.OrdinalIgnoreCase);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var original = await db.RefreshTokens.SingleAsync(
                token => token.TokenHash == tokenService.HashRefreshToken(originalToken));
            var replacement = await db.RefreshTokens.SingleAsync(
                token => token.TokenHash == tokenService.HashRefreshToken(replacementToken));

            Assert.Equal(original.FamilyId, replacement.FamilyId);
            Assert.True(original.IsRevoked);
            Assert.Equal(replacement.Id, original.ReplacedByTokenId);
            Assert.True(replacement.IsActive);
        }

        var replayResponse = await PostRefreshAsync(originalToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var familyId = await db.RefreshTokens
                .Where(token => token.TokenHash == tokenService.HashRefreshToken(originalToken))
                .Select(token => token.FamilyId)
                .SingleAsync();
            var family = await db.RefreshTokens.Where(token => token.FamilyId == familyId).ToListAsync();

            Assert.All(family, token => Assert.True(token.IsRevoked));
        }

        var replacementResponse = await PostRefreshAsync(replacementToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replacementResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_UsesCookieOnlyAndIgnoresBodyAndQueryTokenValues()
    {
        var email = $"refresh-cookie-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";
        await RegisterAsync(email, password);
        var cookieToken = await LoginAndGetRefreshTokenAsync(email, password);
        var unselectedToken = await LoginAndGetRefreshTokenAsync(email, password);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/auth/refresh?refreshToken={Uri.EscapeDataString(unselectedToken)}")
        {
            Content = JsonContent.Create(new { refreshToken = unselectedToken })
        };
        request.Headers.Add("Cookie", $"refreshToken={cookieToken}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var unselectedRecord = await db.RefreshTokens.SingleAsync(
            token => token.TokenHash == tokenService.HashRefreshToken(unselectedToken));
        Assert.True(unselectedRecord.IsActive);
        Assert.Null(unselectedRecord.ReplacedByTokenId);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorizedAndDeletesCookie()
    {
        var response = await _client.PostAsync("/api/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var deletedCookie = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.NotNull(deletedCookie);
        Assert.Contains("path=/api/auth", deletedCookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            deletedCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            deletedCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    private async Task RegisterAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string> LoginAndGetRefreshTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return GetRefreshTokenCookie(response);
    }

    private Task<HttpResponseMessage> PostRefreshAsync(string rawToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={rawToken}");
        return _client.SendAsync(request);
    }

    private static string GetRefreshTokenCookie(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.NotNull(header);
        return header.Split(';')[0]["refreshToken=".Length..];
    }
}
