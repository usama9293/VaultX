using Moq;
using PasswordManager.Application.Features.Authentication.Logout;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using Xunit;

namespace PasswordManager.UnitTests.Application;

public class LogoutUserHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly Mock<ITokenService> _tokenServiceMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly LogoutUserHandler _handler;

    public LogoutUserHandlerTests()
    {
        _unitOfWorkMock
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByFamilyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken>());

        _handler = new LogoutUserHandler(
            _refreshTokenRepositoryMock.Object,
            _tokenServiceMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.HandleAsync(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_NullOrWhitespaceToken_ReturnsWithoutInteractingWithRepository(string? rawToken)
    {
        var command = new LogoutCommand(rawToken);

        await _handler.HandleAsync(command);

        _tokenServiceMock.Verify(t => t.HashRefreshToken(It.IsAny<string>()), Times.Never);
        _refreshTokenRepositoryMock.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidActiveToken_RevokesMatchingSessionAndSaves()
    {
        const string rawToken = "valid-raw-refresh-token";
        const string tokenHash = "hashed-token-value-64-chars";
        var userId = Guid.NewGuid();
        var token = new RefreshToken(userId, tokenHash, DateTime.UtcNow.AddDays(7));

        _tokenServiceMock
            .Setup(t => t.HashRefreshToken(rawToken))
            .Returns(tokenHash);

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByFamilyIdAsync(token.FamilyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { token });

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var command = new LogoutCommand(rawToken);

        await _handler.HandleAsync(command);

        Assert.True(token.IsRevoked);
        Assert.NotNull(token.RevokedAt);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TokenNotFound_DoesNotThrowAndDoesNotSave()
    {
        const string rawToken = "nonexistent-token";
        const string tokenHash = "nonexistent-hash";

        _tokenServiceMock
            .Setup(t => t.HashRefreshToken(rawToken))
            .Returns(tokenHash);

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var command = new LogoutCommand(rawToken);

        await _handler.HandleAsync(command);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AlreadyRevokedToken_DoesNotSaveAgain()
    {
        const string rawToken = "revoked-token";
        const string tokenHash = "revoked-hash";
        var token = new RefreshToken(Guid.NewGuid(), tokenHash, DateTime.UtcNow.AddDays(7));
        token.Revoke();
        var originalRevocationTime = token.RevokedAt;

        _tokenServiceMock
            .Setup(t => t.HashRefreshToken(rawToken))
            .Returns(tokenHash);

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var command = new LogoutCommand(rawToken);

        await _handler.HandleAsync(command);

        Assert.Equal(originalRevocationTime, token.RevokedAt);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExpiredToken_DoesNotRevokeOrSave()
    {
        const string rawToken = "expired-token";
        const string tokenHash = "expired-hash";
        var token = new RefreshToken(Guid.NewGuid(), tokenHash, DateTime.UtcNow.AddMilliseconds(50));
        Thread.Sleep(60);

        _tokenServiceMock
            .Setup(t => t.HashRefreshToken(rawToken))
            .Returns(tokenHash);

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var command = new LogoutCommand(rawToken);

        await _handler.HandleAsync(command);

        Assert.True(token.IsExpired);
        Assert.False(token.IsRevoked);
        Assert.Null(token.RevokedAt);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MalformedTokenCausingArgumentException_DoesNotThrowAndDoesNotSave()
    {
        const string malformedToken = "invalid-token";

        _tokenServiceMock
            .Setup(t => t.HashRefreshToken(malformedToken))
            .Throws(new ArgumentException("Malformed token"));

        var command = new LogoutCommand(malformedToken);

        await _handler.HandleAsync(command);

        _refreshTokenRepositoryMock.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
