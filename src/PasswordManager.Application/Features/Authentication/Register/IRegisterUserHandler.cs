using PasswordManager.Application.DTOs.Authentication;

namespace PasswordManager.Application.Features.Authentication.Register;

public interface IRegisterUserHandler
{
    Task<UserResponse> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken = default);
}
