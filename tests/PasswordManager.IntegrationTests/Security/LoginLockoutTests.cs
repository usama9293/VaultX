using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public class LoginLockoutTests
{
    [Fact]
    public async Task FifthFailureLocksAccountAndLockedLoginRemainsGeneric()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"locked-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@Lockout2026!";
        await RegisterAsync(client, email, password);

        var failures = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            failures.Add(await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(email, "WrongPassword2026!")));
        }

        Assert.All(failures, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        var lockedResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);
        Assert.False(lockedResponse.Headers.Contains("Set-Cookie"));

        var expectedBody = await failures[0].Content.ReadAsStringAsync();
        foreach (var response in failures.Skip(1).Append(lockedResponse))
        {
            Assert.Equal(expectedBody, await response.Content.ReadAsStringAsync());
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(account => account.Email == email);
        Assert.Equal(5, user.FailedLoginAttempts);
        Assert.True(user.LockedUntil > DateTime.UtcNow);
        Assert.Equal(0, await db.RefreshTokens.CountAsync(token => token.UserId == user.Id));
    }

    [Fact]
    public async Task SuccessfulLoginAfterLockExpirationLazilyResetsFailures()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"expired-lock-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@Lockout2026!";
        await RegisterAsync(client, email, password);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(email, "WrongPassword2026!"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users
                .Where(account => account.Email == email)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(account => account.LockedUntil, DateTime.UtcNow.AddMinutes(-1)));
        }

        var success = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedUser = await verificationDb.Users.SingleAsync(account => account.Email == email);
        Assert.Equal(0, persistedUser.FailedLoginAttempts);
        Assert.Null(persistedUser.LockedUntil);
        Assert.Equal(1, await verificationDb.RefreshTokens.CountAsync(token => token.UserId == persistedUser.Id));
    }

    [Fact]
    public async Task UnknownAndLockedAccountsReturnSameGeneric401WithoutSession()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"enumeration-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@Lockout2026!";
        await RegisterAsync(client, email, password);

        HttpResponseMessage? lockedResponse = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            lockedResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(email, "WrongPassword2026!"));
        }

        var unknownResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest($"unknown-{Guid.NewGuid():N}@vaultx.local", "WrongPassword2026!"));

        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse!.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownResponse.StatusCode);
        var lockedBody = await lockedResponse.Content.ReadAsStringAsync();
        Assert.Equal(lockedBody, await unknownResponse.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(lockedBody);
        Assert.Equal("Invalid email or password.", problem.RootElement.GetProperty("detail").GetString());
    }

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
