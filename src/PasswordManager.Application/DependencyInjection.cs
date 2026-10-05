using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Features.Authentication.Login;
using PasswordManager.Application.Features.Authentication.Logout;
using PasswordManager.Application.Features.Authentication.Register;
using PasswordManager.Application.Features.Users.GetById;

namespace PasswordManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRegisterUserHandler, RegisterUserHandler>();
        services.AddScoped<ILoginUserHandler, LoginUserHandler>();
        services.AddScoped<ILogoutUserHandler, LogoutUserHandler>();
        services.AddScoped<IGetUserByIdHandler, GetUserByIdHandler>();
        return services;
    }
}
