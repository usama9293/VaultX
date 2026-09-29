using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Application.Interfaces.Security;
using PasswordManager.Infrastructure.Persistence;
using PasswordManager.Infrastructure.Repositories;
using PasswordManager.Infrastructure.Services;

namespace PasswordManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        return services;
    }
}
