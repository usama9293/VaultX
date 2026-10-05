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

        var discoveredToken = await _refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);
        if (discoveredToken is null)
        {
            return;
        }

        await _unitOfWork.ExecuteInTransactionAsync(
            async transactionToken =>
            {
                await _refreshTokenRepository.LockUserAsync(discoveredToken.UserId, transactionToken);

                var refreshToken = await _refreshTokenRepository.GetByHashAsync(tokenHash, transactionToken);
                if (refreshToken is null)
                {
                    return false;
                }

                var familyTokens = await _refreshTokenRepository.GetByFamilyIdAsync(
                    refreshToken.FamilyId,
                    transactionToken);
                var familyChanged = false;
                foreach (var familyToken in familyTokens)
                {
                    if (familyToken.IsActive)
                    {
                        familyToken.Revoke();
                        familyChanged = true;
                    }
                }

                if (familyChanged)
                {
                    await _unitOfWork.SaveChangesAsync(transactionToken);
                }

                return familyChanged;
            },
            cancellationToken);
    }
}
