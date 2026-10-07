using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PasswordManager.API;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests;

public sealed class PostgresWebApplicationFactory : WebApplicationFactory<Program>
{
    private string? _adminConnectionString;
    private string? _isolatedDatabaseName;
    private string? _testConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestJwtSettings.SecretKey,
                ["RateLimiting:Global:PermitLimit"] = "10000",
                ["RateLimiting:Login:PermitLimit"] = "1000",
                ["RateLimiting:Registration:PermitLimit"] = "1000",
                ["RateLimiting:Refresh:PermitLimit"] = "1000"
            });
        });

        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VAULTX_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "Set VAULTX_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL concurrency tests.");
        }

        var configured = new NpgsqlConnectionStringBuilder(configuredConnectionString);
        _adminConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        _isolatedDatabaseName = $"vaultx_phase23_test_{Guid.NewGuid():N}";
        _testConnectionString = new NpgsqlConnectionStringBuilder(configured.ConnectionString)
        {
            Database = _isolatedDatabaseName
        }.ConnectionString;

        CreateIsolatedDatabase();

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(_testConnectionString));

            try
            {
                using var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.Migrate();
            }
            catch
            {
                DropIsolatedDatabase();
                throw;
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            DropIsolatedDatabase();
        }
    }

    private void CreateIsolatedDatabase()
    {
        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_isolatedDatabaseName}\"";
        command.ExecuteNonQuery();
    }

    private void DropIsolatedDatabase()
    {
        if (_adminConnectionString is null || _isolatedDatabaseName is null)
        {
            return;
        }

        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE \"{_isolatedDatabaseName}\" WITH (FORCE)";
        command.ExecuteNonQuery();

        _isolatedDatabaseName = null;
    }
}
