using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public class RateLimitingTests
{
    [Fact]
    public async Task LoginLimit_ReturnsGeneric429AndRejectedRequestDoesNotMutateAuthenticationState()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"rate-login-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@RateLimit2026!";
        await RegisterAsync(client, email, password);

        var first = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword2026!"));
        var second = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword2026!"));
        var rejected = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        if (rejected.Headers.TryGetValues("Retry-After", out var retryAfterValues))
        {
            Assert.True(int.TryParse(retryAfterValues.Single(), out var retryAfter));
            Assert.True(retryAfter > 0);
        }
        using (var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()))
        {
            Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
            Assert.Contains("rate limit", body.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(email, body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(account => account.Email == email);
        Assert.Equal(2, user.FailedLoginAttempts);
        Assert.Equal(0, await db.RefreshTokens.CountAsync(token => token.UserId == user.Id));

        var registration = await RegisterAsync(
            client,
            $"rate-independent-{Guid.NewGuid():N}@vaultx.local",
            password);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
    }

    [Fact]
    public async Task RegistrationLimit_RejectsTheRequestAfterConfiguredPermit()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient();
        const string password = "VaultX@RateLimit2026!";

        var first = await RegisterAsync(client, $"rate-register1-{Guid.NewGuid():N}@vaultx.local", password);
        var second = await RegisterAsync(client, $"rate-register2-{Guid.NewGuid():N}@vaultx.local", password);
        var thirdEmail = $"rate-register3-{Guid.NewGuid():N}@vaultx.local";
        var third = await RegisterAsync(client, thirdEmail, password);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(user => user.Email == thirdEmail));
    }

    [Fact]
    public async Task RefreshLimit_DoesNotConsumeTokenForRejectedRequest_AndLogoutRemainsAvailable()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"rate-refresh-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@RateLimit2026!";
        await RegisterAsync(client, email, password);
        var originalToken = await LoginAndReadRefreshCookieAsync(client, email, password);
        var firstRefresh = await RefreshAsync(client, originalToken);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var replacementToken = GetRefreshTokenCookie(firstRefresh);
        var secondRefresh = await RefreshAsync(client, replacementToken);
        Assert.Equal(HttpStatusCode.OK, secondRefresh.StatusCode);
        var secondReplacementToken = GetRefreshTokenCookie(secondRefresh);

        var limitedRefresh = await RefreshAsync(client, secondReplacementToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedRefresh.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var lastIssuedToken = await db.RefreshTokens.SingleAsync(
                token => token.TokenHash == tokenService.HashRefreshToken(secondReplacementToken));
            Assert.True(lastIssuedToken.IsActive);
            Assert.Null(lastIssuedToken.ReplacedByTokenId);
        }

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", $"refreshToken={secondReplacementToken}");
        var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task GlobalLimit_IsPartitionedByNormalizedRemoteIp()
    {
        using var factory = new RateLimitingWebApplicationFactory(globalLimit: 2);
        using var client = factory.CreateClient();

        var firstIpFirst = await GetRootAsync(client, "203.0.113.17");
        var firstIpSecond = await GetRootAsync(client, "203.0.113.17");
        var firstIpRejected = await GetRootAsync(client, "203.0.113.17");
        var secondIpAllowed = await GetRootAsync(client, "198.51.100.8");

        Assert.Equal(HttpStatusCode.OK, firstIpFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstIpSecond.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstIpRejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondIpAllowed.StatusCode);
    }

    [Fact]
    public async Task LoginSlidingWindow_ReplenishesAfterTheConfiguredWindow()
    {
        using var factory = new RateLimitingWebApplicationFactory(loginWindowSeconds: 1);
        using var client = factory.CreateClient();
        const string email = "window-reset@vaultx.local";

        var first = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong"));
        var second = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong"));
        var rejected = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(1250));

        var afterWindow = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, afterWindow.StatusCode);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password)
    {
        return await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
    }

    private static async Task<string> LoginAndReadRefreshCookieAsync(
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return GetRefreshTokenCookie(response);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={refreshToken}");
        return client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetRootAsync(HttpClient client, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Remote-IP", ip);
        return await client.SendAsync(request);
    }

    private static string GetRefreshTokenCookie(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        return cookie.Split(';')[0]["refreshToken=".Length..];
    }
}
