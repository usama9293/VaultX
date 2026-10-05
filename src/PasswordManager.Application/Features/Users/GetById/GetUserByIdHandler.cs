using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;

namespace PasswordManager.Application.Features.Users.GetById;

public sealed class GetUserByIdHandler : IGetUserByIdHandler
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;

    public GetUserByIdHandler(ICurrentUser currentUser, IUserRepository userRepository)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
    }

    public async Task<UserResponse?> HandleAsync(
        GetUserByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var currentUserId = _currentUser.UserId;
        if (currentUserId is null || query.UserId != currentUserId.Value)
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return new UserResponse(user.Id, user.Email, user.CreatedAt, user.UpdatedAt);
    }
}
