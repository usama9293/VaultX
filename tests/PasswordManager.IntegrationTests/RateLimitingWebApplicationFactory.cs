using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PasswordManager.IntegrationTests;

public sealed class RateLimitingWebApplicationFactory : CustomWebApplicationFactory
{
    private readonly int _globalLimit;
    private readonly int _endpointLimit;
    private readonly int _loginWindowSeconds;

    public RateLimitingWebApplicationFactory(
        int globalLimit = 1000,
        int endpointLimit = 2,
        int loginWindowSeconds = 2)
    {
        _globalLimit = globalLimit;
        _endpointLimit = endpointLimit;
        _loginWindowSeconds = loginWindowSeconds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Global:PermitLimit"] = _globalLimit.ToString(),
                ["RateLimiting:Global:WindowSeconds"] = "60",
                ["RateLimiting:Global:SegmentsPerWindow"] = "6",
                ["RateLimiting:Login:PermitLimit"] = _endpointLimit.ToString(),
                ["RateLimiting:Login:WindowSeconds"] = _loginWindowSeconds.ToString(),
                ["RateLimiting:Login:SegmentsPerWindow"] = "1",
                ["RateLimiting:Registration:PermitLimit"] = _endpointLimit.ToString(),
                ["RateLimiting:Registration:WindowSeconds"] = "3600",
                ["RateLimiting:Registration:SegmentsPerWindow"] = "1",
                ["RateLimiting:Refresh:PermitLimit"] = _endpointLimit.ToString(),
                ["RateLimiting:Refresh:WindowSeconds"] = "60",
                ["RateLimiting:Refresh:SegmentsPerWindow"] = "1"
            });
        });
        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>());
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (IPAddress.TryParse(
                            context.Request.Headers["X-Test-Remote-IP"],
                            out var testRemoteAddress))
                    {
                        context.Connection.RemoteIpAddress = testRemoteAddress;
                    }

                    await nextMiddleware();
                });

                next(app);
            };
        }
    }
}
