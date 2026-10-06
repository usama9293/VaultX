using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public sealed class VaultInitializationConcurrencyTests : IClassFixture<PostgresWebApplicationFactory>
{
    private const long InsertBarrierLockKey = 740219331;
    private readonly PostgresWebApplicationFactory _factory;

    public VaultInitializationConcurrencyTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentInitialization_ContendsAtPostgresInsertAndCreatesOneVault()
    {
        var (userId, accessToken) = await CreateAuthenticatedUserAsync();
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            connectionString = db.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The PostgreSQL test connection string is unavailable.");
        }

        await InstallInsertBarrierAsync(connectionString);
        await using var barrierConnection = new NpgsqlConnection(connectionString);
        await barrierConnection.OpenAsync();
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_lock(@lockKey)",
            barrierConnection))
        {
            lockCommand.Parameters.AddWithValue("lockKey", InsertBarrierLockKey);
            await lockCommand.ExecuteNonQueryAsync();
        }

        var barrierLocked = true;
        try
        {
            using var clientA = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var clientB = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var requestA = CreateInitializationRequest(accessToken);
            using var requestB = CreateInitializationRequest(accessToken);
            var initializationA = clientA.SendAsync(requestA);
            var initializationB = clientB.SendAsync(requestB);

            try
            {
                await WaitForVaultInsertWaitersAsync(connectionString, expectedWaiters: 2);
            }
            finally
            {
                await ReleaseInsertBarrierAsync(barrierConnection);
                barrierLocked = false;
            }

            using var responseA = await initializationA;
            using var responseB = await initializationB;
            Assert.Contains(responseA.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            Assert.Contains(responseB.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            Assert.Single(new[] { responseA, responseB }, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(new[] { responseA, responseB }, response => response.StatusCode == HttpStatusCode.OK);

            using var jsonA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
            using var jsonB = JsonDocument.Parse(await responseB.Content.ReadAsStringAsync());
            var vaultIdA = jsonA.RootElement.GetProperty("id").GetGuid();
            var vaultIdB = jsonB.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(vaultIdA, vaultIdB);

            using var verificationScope = _factory.Services.CreateScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await verificationDb.Vaults.CountAsync(vault => vault.UserId == userId));
            Assert.Equal(vaultIdA, await verificationDb.Vaults
                .Where(vault => vault.UserId == userId)
                .Select(vault => vault.Id)
                .SingleAsync());
        }
        finally
        {
            if (barrierLocked)
            {
                await ReleaseInsertBarrierAsync(barrierConnection);
            }

            await RemoveInsertBarrierAsync(connectionString);
        }
    }

    private async Task<(Guid UserId, string AccessToken)> CreateAuthenticatedUserAsync()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"vault-race-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@VaultPhase3_2026!";
        using var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var user = await registration.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return (user.Id, json.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("The login response omitted the access token."));
    }

    private static HttpRequestMessage CreateInitializationRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vault");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task InstallInsertBarrierAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE FUNCTION vaultx_test_vault_insert_barrier() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock({InsertBarrierLockKey});
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER vaultx_test_vault_insert_barrier
            BEFORE INSERT ON "Vaults"
            FOR EACH ROW EXECUTE FUNCTION vaultx_test_vault_insert_barrier();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RemoveInsertBarrierAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TRIGGER IF EXISTS vaultx_test_vault_insert_barrier ON "Vaults";
            DROP FUNCTION IF EXISTS vaultx_test_vault_insert_barrier();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ReleaseInsertBarrierAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(@lockKey)",
            connection);
        command.Parameters.AddWithValue("lockKey", InsertBarrierLockKey);
        Assert.True((bool)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The PostgreSQL insert barrier lock was not held.")));
    }

    private static async Task WaitForVaultInsertWaitersAsync(string connectionString, int expectedWaiters)
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
                  AND wait_event = 'advisory'
                  AND query LIKE 'INSERT INTO "Vaults"%'
                """;

            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= expectedWaiters)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"Expected {expectedWaiters} independent PostgreSQL Vault INSERT statements at the test barrier.");
    }
}
