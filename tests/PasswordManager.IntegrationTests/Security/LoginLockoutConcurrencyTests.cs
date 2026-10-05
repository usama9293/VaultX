using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public class LoginLockoutConcurrencyTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PostgresWebApplicationFactory _factory;

    public LoginLockoutConcurrencyTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task ConcurrentFailuresAtThreshold_SerializeAtPostgresLockAndDoNotLoseLockout()
    {
        var email = $"lockout-race-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@PostgresLock2026!";
        var registration = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        Guid userId;
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(account => account.Email == email);
            var now = DateTime.UtcNow;
            for (var attempt = 0; attempt < User.LoginFailureThreshold - 1; attempt++)
            {
                user.RecordFailedLogin(now);
            }

            await db.SaveChangesAsync();
            userId = user.Id;
            connectionString = db.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The PostgreSQL test connection string is unavailable.");
        }

        await using var blockerConnection = new NpgsqlConnection(connectionString);
        await blockerConnection.OpenAsync();
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        await using (var blockerCommand = blockerConnection.CreateCommand())
        {
            blockerCommand.Transaction = blockerTransaction;
            blockerCommand.CommandText = "SELECT 1 FROM \"Users\" WHERE \"Id\" = @userId FOR UPDATE";
            blockerCommand.Parameters.AddWithValue("userId", userId);
            Assert.NotNull(await blockerCommand.ExecuteScalarAsync());
        }

        var requests = new[]
        {
            CreateFailedLoginRequest(email),
            CreateFailedLoginRequest(email)
        };
        var responsesPending = requests.Select(_client.SendAsync).ToArray();

        try
        {
            await WaitForUserRowLockWaitersAsync(connectionString, expectedWaiters: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        var responses = await Task.WhenAll(responsesPending);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.All(responses, response => Assert.False(response.Headers.Contains("Set-Cookie")));
        var firstBody = await responses[0].Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password.", firstBody, StringComparison.Ordinal);
        Assert.Equal(firstBody, await responses[1].Content.ReadAsStringAsync());

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedUser = await verificationDb.Users.SingleAsync(account => account.Id == userId);
        Assert.Equal(User.LoginFailureThreshold, persistedUser.FailedLoginAttempts);
        Assert.True(persistedUser.LockedUntil > DateTime.UtcNow);
        Assert.Equal(0, await verificationDb.RefreshTokens.CountAsync(token => token.UserId == userId));
    }

    private HttpRequestMessage CreateFailedLoginRequest(string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, "WrongPassword2026!"))
        };
        return request;
    }

    private static async Task WaitForUserRowLockWaitersAsync(string connectionString, int expectedWaiters)
    {
        await using var monitorConnection = new NpgsqlConnection(connectionString);
        await monitorConnection.OpenAsync();

        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            await using var command = monitorConnection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND state = 'active'
                  AND wait_event_type = 'Lock'
                  AND query LIKE 'SELECT 1 FROM "Users" WHERE "Id"%'
                """;

            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= expectedWaiters)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"Expected {expectedWaiters} failed-login requests waiting on the PostgreSQL user-row lock.");
    }
}
