using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Features.Authentication.Login;
using PasswordManager.Application.Features.Authentication.Logout;
using PasswordManager.Application.Features.Authentication.Register;
using PasswordManager.Application.Features.Authentication.Refresh;
using PasswordManager.Application.Features.Users.GetById;
using PasswordManager.Application.Features.Vault.GetCurrent;
using PasswordManager.Application.Features.Vault.Initialize;
using PasswordManager.Application.Features.Vault.Entries;

namespace PasswordManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRegisterUserHandler, RegisterUserHandler>();
        services.AddScoped<ILoginUserHandler, LoginUserHandler>();
        services.AddScoped<ILogoutUserHandler, LogoutUserHandler>();
        services.AddScoped<IRefreshTokenHandler, RefreshTokenHandler>();
        services.AddScoped<IGetUserByIdHandler, GetUserByIdHandler>();
        services.AddScoped<IInitializeVaultHandler, InitializeVaultHandler>();
        services.AddScoped<IGetCurrentVaultHandler, GetCurrentVaultHandler>();
        services.AddScoped<IVaultEntryService, VaultEntryService>();
        return services;
    }
}
