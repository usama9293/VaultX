using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Features.Authentication.Refresh;

public sealed class RefreshTokenHandler : IRefreshTokenHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTokenHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        IUnitOfWork unitOfWork)
    {
        _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<RefreshResult> HandleAsync(
        RefreshCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.RawRefreshToken))
        {
            throw InvalidRefreshToken();
        }

        var tokenHash = _tokenService.HashRefreshToken(command.RawRefreshToken);
        var discoveredToken = await _refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);
        if (discoveredToken is null)
        {
            throw InvalidRefreshToken();
        }

        var result = await _unitOfWork.ExecuteInTransactionAsync(
            async transactionToken =>
            {
                await _refreshTokenRepository.LockUserAsync(discoveredToken.UserId, transactionToken);

                var refreshToken = await _refreshTokenRepository.GetByHashAsync(tokenHash, transactionToken);
                if (refreshToken is null)
                {
                    return null;
                }

                if (refreshToken.IsRevoked)
                {
                    if (refreshToken.ReplacedByTokenId is not null)
                    {
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
                    }

                    return null;
                }

                if (refreshToken.IsExpired)
                {
                    return null;
                }

                var user = refreshToken.User;
                var (accessToken, accessTokenExpiresAt) = _tokenService.GenerateAccessToken(user);
                var (rawRefreshToken, replacementHash, replacementExpiresAt) =
                    _tokenService.GenerateRefreshToken();
                var replacement = new RefreshToken(
                    refreshToken.UserId,
                    refreshToken.FamilyId,
                    replacementHash,
                    replacementExpiresAt);

                refreshToken.Revoke(replacement.Id);
                await _refreshTokenRepository.UpdateAsync(refreshToken, transactionToken);
                await _refreshTokenRepository.AddAsync(replacement, transactionToken);
                await _unitOfWork.SaveChangesAsync(transactionToken);

                return new RefreshResult(
                    accessToken,
                    accessTokenExpiresAt,
                    rawRefreshToken,
                    replacementExpiresAt);
            },
            cancellationToken);

        return result ?? throw InvalidRefreshToken();
    }

    private static InvalidCredentialsException InvalidRefreshToken()
    {
        return new InvalidCredentialsException();
    }
}
