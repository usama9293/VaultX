using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using PasswordManager.API;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private string? _databasePath;

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

        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(CustomWebApplicationFactory).Assembly);

            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));

            _databasePath = Path.Combine(Path.GetTempPath(), $"vaultx-integration-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(connectionString);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && _databasePath is not null && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
