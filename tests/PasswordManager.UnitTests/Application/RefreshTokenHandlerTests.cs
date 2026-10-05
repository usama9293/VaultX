using Moq;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Authentication.Refresh;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using Xunit;

namespace PasswordManager.UnitTests.Application;

public class RefreshTokenHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _repository = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly RefreshTokenHandler _handler;

    public RefreshTokenHandlerTests()
    {
        _unitOfWork
            .Setup(unit => unit.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<RefreshResult?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<RefreshResult?>> operation, CancellationToken token) =>
                operation(token)!);

        _unitOfWork
            .Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _repository
            .Setup(repository => repository.GetByFamilyIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken>());

        _handler = new RefreshTokenHandler(_repository.Object, _tokenService.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task HandleAsync_ActiveToken_RotatesWithinSameFamily()
    {
        const string rawToken = "presented-refresh-token";
        const string tokenHash = "presented-hash";
        const string replacementRawToken = "replacement-refresh-token";
        const string replacementHash = "replacement-hash";
        var user = new User("owner@example.test", new byte[] { 1, 2, 3 });
        var token = new RefreshToken(user.Id, tokenHash, DateTime.UtcNow.AddDays(7));
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(token, user);
        var accessTokenExpiry = DateTime.UtcNow.AddMinutes(15);
        var refreshTokenExpiry = DateTime.UtcNow.AddDays(14);
        RefreshToken? replacement = null;

        _tokenService.Setup(service => service.HashRefreshToken(rawToken)).Returns(tokenHash);
        _tokenService.Setup(service => service.GenerateAccessToken(user))
            .Returns(("new-access-token", accessTokenExpiry));
        _tokenService.Setup(service => service.GenerateRefreshToken())
            .Returns((replacementRawToken, replacementHash, refreshTokenExpiry));
        _repository.Setup(repository => repository.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _repository.Setup(repository => repository.AddAsync(
                It.IsAny<RefreshToken>(),
                It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((value, _) => replacement = value)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(new RefreshCommand(rawToken));

        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal(replacementRawToken, result.RawRefreshToken);
        Assert.True(token.IsRevoked);
        Assert.NotNull(token.ReplacedByTokenId);
        Assert.NotNull(replacement);
        Assert.Equal(token.FamilyId, replacement.FamilyId);
        Assert.Equal(replacement.Id, token.ReplacedByTokenId);
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EmptyToken_ThrowsGenericInvalidCredentialsWithoutLookup()
    {
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _handler.HandleAsync(new RefreshCommand(" ")));

        _tokenService.Verify(service => service.HashRefreshToken(It.IsAny<string>()), Times.Never);
        _repository.Verify(repository => repository.GetByHashAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnknownToken_ThrowsGenericInvalidCredentials()
    {
        _tokenService.Setup(service => service.HashRefreshToken("unknown")).Returns("unknown-hash");
        _repository.Setup(repository => repository.GetByHashAsync(
                "unknown-hash",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _handler.HandleAsync(new RefreshCommand("unknown")));

        _unitOfWork.Verify(unit => unit.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<RefreshResult?>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExpiredToken_ThrowsWithoutCreatingReplacement()
    {
        const string rawToken = "expired";
        const string tokenHash = "expired-hash";
        var token = new RefreshToken(Guid.NewGuid(), tokenHash, DateTime.UtcNow.AddMilliseconds(30));
        await Task.Delay(50);
        _tokenService.Setup(service => service.HashRefreshToken(rawToken)).Returns(tokenHash);
        _repository.Setup(repository => repository.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _handler.HandleAsync(new RefreshCommand(rawToken)));

        _tokenService.Verify(service => service.GenerateRefreshToken(), Times.Never);
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReplayedRotatedToken_RevokesActiveFamilyAndFails()
    {
        const string rawToken = "replayed-token";
        const string tokenHash = "replayed-hash";
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var replayedToken = new RefreshToken(userId, familyId, tokenHash, DateTime.UtcNow.AddDays(7));
        replayedToken.Revoke(Guid.NewGuid());
        var activeSuccessor = new RefreshToken(userId, familyId, "successor-hash", DateTime.UtcNow.AddDays(7));

        _tokenService.Setup(service => service.HashRefreshToken(rawToken)).Returns(tokenHash);
        _repository.Setup(repository => repository.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(replayedToken);
        _repository.Setup(repository => repository.GetByFamilyIdAsync(familyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { replayedToken, activeSuccessor });

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _handler.HandleAsync(new RefreshCommand(rawToken)));

        Assert.True(activeSuccessor.IsRevoked);
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
