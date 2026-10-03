using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;

namespace PasswordManager.Application.Features.Authentication.Logout;

public class LogoutUserHandler : ILogoutUserHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutUserHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        IUnitOfWork unitOfWork)
    {
        _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.RawRefreshToken))
        {
            return;
        }

        string tokenHash;
        try
        {
            tokenHash = _tokenService.HashRefreshToken(command.RawRefreshToken);
        }
        catch (ArgumentException)
        {
            return;
        }

        var refreshToken = await _refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);
        if (refreshToken is not null && refreshToken.IsActive)
        {
            refreshToken.Revoke();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
