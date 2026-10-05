using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PasswordManager.API;

namespace PasswordManager.IntegrationTests.Security;

public class JwtConfigurationTests
{
    [Fact]
    public void MissingJwtSigningKey_FailsApplicationStartup()
    {
        using var factory = new MissingJwtKeyWebApplicationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("JwtSettings:SecretKey", GetExceptionMessages(exception), StringComparison.Ordinal);
    }

    private static string GetExceptionMessages(Exception exception)
    {
        return exception is AggregateException aggregateException
            ? string.Join(Environment.NewLine, aggregateException.Flatten().InnerExceptions.SelectMany(GetExceptionMessages))
            : string.Join(
                Environment.NewLine,
                exception.Message,
                exception.InnerException is null ? string.Empty : GetExceptionMessages(exception.InnerException));
    }

    private sealed class MissingJwtKeyWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:SecretKey"] = string.Empty
                });
            });
        }
    }
}
