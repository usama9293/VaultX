using PasswordManager.Application.DTOs.Authentication;

namespace PasswordManager.Application.Features.Authentication.Login;

public interface ILoginUserHandler
{
    Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default);
}
