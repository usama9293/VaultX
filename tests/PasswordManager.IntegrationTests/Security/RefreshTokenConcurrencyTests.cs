using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public class RefreshTokenConcurrencyTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PostgresWebApplicationFactory _factory;

    public RefreshTokenConcurrencyTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task ConcurrentRefreshRequestsWithSameToken_ReplayRevokesTheReplacementFamily()
    {
        var email = $"refresh-race-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@PostgresTest2026!";
        var registration = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var originalToken = GetRefreshTokenCookie(login);

        string connectionString;
        Guid userId;
        using (var lookupScope = _factory.Services.CreateScope())
        {
            var lookupDb = lookupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var lookupTokenService = lookupScope.ServiceProvider.GetRequiredService<ITokenService>();
            connectionString = lookupDb.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The PostgreSQL test connection string is unavailable.");

            userId = await lookupDb.RefreshTokens
                .Where(token => token.TokenHash == lookupTokenService.HashRefreshToken(originalToken))
                .Select(token => token.UserId)
                .SingleAsync();
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
            _client.SendAsync(CreateRefreshRequest(originalToken)),
            _client.SendAsync(CreateRefreshRequest(originalToken))
        };

        try
        {
            await WaitForUserRowLockWaitersAsync(connectionString, expectedWaiters: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        var responses = await Task.WhenAll(requests);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        var unauthorizedResponse = responses.Single(response => response.StatusCode == HttpStatusCode.Unauthorized);
        var unauthorizedBody = await unauthorizedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password.", unauthorizedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("FamilyId", unauthorizedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replay", unauthorizedBody, StringComparison.OrdinalIgnoreCase);

        var successfulResponse = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var replacementToken = GetRefreshTokenCookie(successfulResponse);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var originalHash = tokenService.HashRefreshToken(originalToken);
        var replacementHash = tokenService.HashRefreshToken(replacementToken);
        var original = await db.RefreshTokens.SingleAsync(token => token.TokenHash == originalHash);
        var family = await db.RefreshTokens.Where(token => token.FamilyId == original.FamilyId).ToListAsync();
        var replacement = Assert.Single(family, token => token.TokenHash == replacementHash);

        Assert.Equal(2, family.Count);
        Assert.All(family, token => Assert.True(token.IsRevoked));
        Assert.True(original.IsRevoked);
        Assert.Equal(replacement.Id, original.ReplacedByTokenId);
        Assert.True(replacement.IsRevoked);
        Assert.DoesNotContain(family, token => token.IsActive);
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

            var waiterCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waiterCount >= expectedWaiters)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"Expected {expectedWaiters} refresh requests waiting on the PostgreSQL user-row lock.");
    }

    private static HttpRequestMessage CreateRefreshRequest(string rawToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={rawToken}");
        return request;
    }

    private static string GetRefreshTokenCookie(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.NotNull(header);
        return header.Split(';')[0]["refreshToken=".Length..];
    }
}
