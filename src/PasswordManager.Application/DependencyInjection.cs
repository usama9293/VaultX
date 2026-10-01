using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Features.Authentication.Login;
using PasswordManager.Application.Features.Authentication.Register;

namespace PasswordManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRegisterUserHandler, RegisterUserHandler>();
        services.AddScoped<ILoginUserHandler, LoginUserHandler>();
        return services;
    }
}
