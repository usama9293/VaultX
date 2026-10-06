using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public sealed class VaultMetadataMigrationTests
{
    [Fact]
    public async Task Migration_RemovesLegacyKeyColumnsWhenVaultTableIsEmpty()
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VAULTX_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "Set VAULTX_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL migration tests.");
        }

        var configured = new NpgsqlConnectionStringBuilder(configuredConnectionString);
        var databaseName = $"vaultx_vault_migration_empty_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        var testConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        try
        {
            await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
            {
                await adminConnection.OpenAsync();
                await using var createDatabase = adminConnection.CreateCommand();
                createDatabase.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                await createDatabase.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(testConnectionString)
                .Options;

            await using var db = new ApplicationDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20260926165453_InitialCreate");
            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(0, await db.Vaults.CountAsync());
            Assert.Equal(0, await db.Database
                .SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS "Value"
                    FROM information_schema.columns
                    WHERE table_name = 'Vaults'
                      AND column_name IN ('EncryptedKey', 'KeyNonce', 'KeyAuthenticationTag')
                    """)
                .SingleAsync());
        }
        finally
        {
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await using var dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Migration_WaitsForConcurrentVaultInsertAndThenFailsWithoutDroppingKeyMaterial()
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VAULTX_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "Set VAULTX_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL migration tests.");
        }

        var configured = new NpgsqlConnectionStringBuilder(configuredConnectionString);
        var databaseName = $"vaultx_vault_migration_lock_{Guid.NewGuid():N}";
        var migrationApplicationName = $"vaultx-migration-{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        var testConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
        var migrationConnectionString = new NpgsqlConnectionStringBuilder(testConnectionString)
        {
            ApplicationName = migrationApplicationName
        }.ConnectionString;

        try
        {
            await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
            {
                await adminConnection.OpenAsync();
                await using var createDatabase = adminConnection.CreateCommand();
                createDatabase.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                await createDatabase.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(migrationConnectionString)
                .Options;

            await using var db = new ApplicationDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20261005112917_AddUserLoginLockout");

            var userId = Guid.NewGuid();
            var vaultId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Users" ("Id", "Email", "PasswordHash", "CreatedAt", "UpdatedAt")
                VALUES ({userId}, {"migration-lock-test@vaultx.local"}, {new byte[] { 1 }}, {now}, {now})
                """);

            await using (var writerConnection = new NpgsqlConnection(testConnectionString))
            {
                await writerConnection.OpenAsync();
                await using var writerTransaction = await writerConnection.BeginTransactionAsync();
                await using (var insertVault = new NpgsqlCommand(
                    """
                    INSERT INTO "Vaults" (
                        "Id", "UserId", "EncryptedKey", "KeyNonce", "KeyAuthenticationTag", "CreatedAt", "UpdatedAt")
                    VALUES (@id, @userId, @encryptedKey, @nonce, @tag, @createdAt, @updatedAt)
                    """,
                    writerConnection,
                    writerTransaction))
                {
                    insertVault.Parameters.AddWithValue("id", vaultId);
                    insertVault.Parameters.AddWithValue("userId", userId);
                    insertVault.Parameters.AddWithValue("encryptedKey", new byte[] { 4 });
                    insertVault.Parameters.AddWithValue("nonce", new byte[] { 5 });
                    insertVault.Parameters.AddWithValue("tag", new byte[] { 6 });
                    insertVault.Parameters.AddWithValue("createdAt", now);
                    insertVault.Parameters.AddWithValue("updatedAt", now);
                    await insertVault.ExecuteNonQueryAsync();
                }

                var migrationTask = db.Database.MigrateAsync();
                await using (var monitorConnection = new NpgsqlConnection(testConnectionString))
                {
                    await monitorConnection.OpenAsync();
                    await WaitForPendingVaultLockAsync(
                        monitorConnection,
                        migrationApplicationName,
                        migrationTask);
                }

                await writerTransaction.CommitAsync();

                var exception = await Assert.ThrowsAsync<PostgresException>(() => migrationTask);
                Assert.Contains("Cannot remove legacy vault key material", exception.MessageText);
            }

            await using var verificationConnection = new NpgsqlConnection(testConnectionString);
            await verificationConnection.OpenAsync();
            await using var verifyVault = new NpgsqlCommand(
                """
                SELECT "EncryptedKey", "KeyNonce", "KeyAuthenticationTag"
                FROM "Vaults"
                WHERE "Id" = @id
                """,
                verificationConnection);
            verifyVault.Parameters.AddWithValue("id", vaultId);
            await using var reader = await verifyVault.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(new byte[] { 4 }, reader.GetFieldValue<byte[]>(0));
            Assert.Equal(new byte[] { 5 }, reader.GetFieldValue<byte[]>(1));
            Assert.Equal(new byte[] { 6 }, reader.GetFieldValue<byte[]>(2));
        }
        finally
        {
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await using var dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Migration_PreservesLegacyVaultsByFailingBeforeDroppingKeyColumns()
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VAULTX_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "Set VAULTX_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL migration tests.");
        }

        var configured = new NpgsqlConnectionStringBuilder(configuredConnectionString);
        var databaseName = $"vaultx_vault_migration_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        var testConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        try
        {
            await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
            {
                await adminConnection.OpenAsync();
                await using var createDatabase = adminConnection.CreateCommand();
                createDatabase.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                await createDatabase.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(testConnectionString)
                .Options;

            await using var db = new ApplicationDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20260926165453_InitialCreate");

            var userId = Guid.NewGuid();
            var vaultId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Users" ("Id", "Email", "PasswordHash", "CreatedAt", "UpdatedAt")
                VALUES ({userId}, {"migration-test@vaultx.local"}, {new byte[] { 1 }}, {now}, {now})
                """);

            // Legacy byte values satisfy the old schema; the migration must preserve them.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Vaults" (
                    "Id", "UserId", "EncryptedKey", "KeyNonce", "KeyAuthenticationTag", "CreatedAt", "UpdatedAt")
                VALUES ({vaultId}, {userId}, {new byte[] { 1 }}, {new byte[] { 2 }}, {new byte[] { 3 }}, {now}, {now})
                """);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => db.Database.MigrateAsync());

            Assert.Contains("Cannot remove legacy vault key material", exception.MessageText);
            Assert.Equal(1, await db.Vaults.CountAsync());
            Assert.Equal(3, await db.Database
                .SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS "Value"
                    FROM information_schema.columns
                    WHERE table_name = 'Vaults'
                      AND column_name IN ('EncryptedKey', 'KeyNonce', 'KeyAuthenticationTag')
                    """)
                .SingleAsync());
        }
        finally
        {
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await using var dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task WaitForPendingVaultLockAsync(
        NpgsqlConnection monitorConnection,
        string migrationApplicationName,
        Task migrationTask)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (migrationTask.IsCompleted)
            {
                await migrationTask;
                throw new InvalidOperationException("The migration completed before waiting for the Vaults lock.");
            }

            await using var command = monitorConnection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity activity
                    JOIN pg_locks lock ON lock.pid = activity.pid
                    WHERE activity.application_name = @applicationName
                      AND activity.wait_event_type = 'Lock'
                      AND lock.locktype = 'relation'
                      AND lock.relation = (
                          SELECT relation.oid
                          FROM pg_class relation
                          WHERE relation.relname = 'Vaults'
                            AND relation.relnamespace = current_schema()::regnamespace
                      )
                      AND NOT lock.granted
                )
                """;
            command.Parameters.AddWithValue("applicationName", migrationApplicationName);
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The migration did not block while waiting for the Vaults table lock.");
    }
}
