namespace PasswordManager.Application.Features.Authentication.Logout;

public interface ILogoutUserHandler
{
    Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default);
}
