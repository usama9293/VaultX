using PasswordManager.Application.DTOs.Authentication;

namespace PasswordManager.Application.Features.Users.GetById;

public interface IGetUserByIdHandler
{
    Task<UserResponse?> HandleAsync(GetUserByIdQuery query, CancellationToken cancellationToken = default);
}
