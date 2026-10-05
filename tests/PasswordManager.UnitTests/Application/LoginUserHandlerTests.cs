using System.Text;
using Moq;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Authentication.Login;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Application.Interfaces.Security;
using PasswordManager.Domain.Entities;
using Xunit;

namespace PasswordManager.UnitTests.Application;

public class LoginUserHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ITokenService> _tokenServiceMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly LoginUserHandler _handler;

    public LoginUserHandlerTests()
    {
        _unitOfWorkMock
            .Setup(unit => unit.ExecuteInTransactionAsync<LoginResult?>(
                It.IsAny<Func<CancellationToken, Task<LoginResult?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<LoginResult?>> operation, CancellationToken token) =>
                operation(token));

        _unitOfWorkMock
            .Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new LoginUserHandler(
            _userRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _passwordHasherMock.Object,
            _tokenServiceMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsLoginResultAndPersistsHashedRefreshToken()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "StrongPassword123!";
        const string storedHash = "$pbkdf2-sha256$i=100000$salt$hash";
        var user = new User(email, Encoding.UTF8.GetBytes(storedHash));

        var command = new LoginCommand("  User@Example.COM  ", password);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.GetFreshByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPasswordAsync(password, storedHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        const string accessToken = "valid.jwt.access.token";
        var accessExpiresAt = DateTime.UtcNow.AddMinutes(15);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns((accessToken, accessExpiresAt));

        const string rawRefreshToken = "raw-refresh-token-value";
        const string tokenHash = "hashed-refresh-token-value";
        var refreshExpiresAt = DateTime.UtcNow.AddDays(7);
        _tokenServiceMock
            .Setup(t => t.GenerateRefreshToken())
            .Returns((rawRefreshToken, tokenHash, refreshExpiresAt));

        RefreshToken? capturedToken = null;
        _refreshTokenRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((rt, _) => capturedToken = rt)
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(accessToken, result.AccessToken);
        Assert.Equal(accessExpiresAt, result.AccessTokenExpiresAt);
        Assert.Equal(rawRefreshToken, result.RawRefreshToken);
        Assert.Equal(refreshExpiresAt, result.RefreshTokenExpiresAt);

        // Verify refresh token entity
        Assert.NotNull(capturedToken);
        Assert.Equal(user.Id, capturedToken.UserId);
        Assert.NotEqual(Guid.Empty, capturedToken.FamilyId);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(tokenHash, capturedToken.TokenHash);
        Assert.NotEqual(rawRefreshToken, capturedToken.TokenHash); // Raw token is NOT persisted

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NonexistentUser_ThrowsGenericInvalidCredentialsException()
    {
        // Arrange
        var command = new LoginCommand("nonexistent@example.com", "SomePassword123!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("nonexistent@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidCredentialsException>(() => _handler.HandleAsync(command));
        Assert.Equal("Invalid email or password.", ex.Message);

        _passwordHasherMock.Verify(
            h => h.VerifyPasswordAsync(
                "SomePassword123!",
                It.Is<string>(hash => hash.StartsWith("$pbkdf2-sha256$i=100000$", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenServiceMock.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_IncorrectPassword_ThrowsGenericInvalidCredentialsException()
    {
        // Arrange
        const string email = "user@example.com";
        const string wrongPassword = "WrongPassword123!";
        const string storedHash = "$pbkdf2-sha256$i=100000$salt$hash";
        var user = new User(email, Encoding.UTF8.GetBytes(storedHash));

        var command = new LoginCommand(email, wrongPassword);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.GetFreshByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPasswordAsync(wrongPassword, storedHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidCredentialsException>(() => _handler.HandleAsync(command));
        Assert.Equal("Invalid email or password.", ex.Message);
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);

        _tokenServiceMock.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        _tokenServiceMock.Verify(t => t.GenerateRefreshToken(), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_FifthFailedPassword_LocksAccount()
    {
        const string email = "lockout@example.com";
        const string storedHash = "$pbkdf2-sha256$i=100000$salt$hash";
        var user = new User(email, Encoding.UTF8.GetBytes(storedHash));
        for (var attempt = 0; attempt < User.LoginFailureThreshold - 1; attempt++)
        {
            user.RecordFailedLogin(DateTime.UtcNow);
        }

        _userRepositoryMock.Setup(repository => repository.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock.Setup(repository => repository.GetFreshByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(hasher => hasher.VerifyPasswordAsync(
                "wrong",
                storedHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _handler.HandleAsync(new LoginCommand(email, "wrong")));

        Assert.Equal("Invalid email or password.", exception.Message);
        Assert.Equal(User.LoginFailureThreshold, user.FailedLoginAttempts);
        Assert.True(user.IsLocked(DateTime.UtcNow));
        _tokenServiceMock.Verify(service => service.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        _refreshTokenRepositoryMock.Verify(repository => repository.AddAsync(
            It.IsAny<RefreshToken>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CorrectPasswordWhileLocked_ReturnsGenericFailureWithoutTokens()
    {
        const string email = "active-lock@example.com";
        const string password = "CorrectPassword123!";
        const string storedHash = "$pbkdf2-sha256$i=100000$salt$hash";
        var user = new User(email, Encoding.UTF8.GetBytes(storedHash));
        for (var attempt = 0; attempt < User.LoginFailureThreshold; attempt++)
        {
            user.RecordFailedLogin(DateTime.UtcNow);
        }

        _userRepositoryMock.Setup(repository => repository.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock.Setup(repository => repository.GetFreshByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(hasher => hasher.VerifyPasswordAsync(
                password,
                storedHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _handler.HandleAsync(new LoginCommand(email, password)));

        Assert.Equal("Invalid email or password.", exception.Message);
        Assert.Equal(User.LoginFailureThreshold, user.FailedLoginAttempts);
        Assert.True(user.IsLocked(DateTime.UtcNow));
        _tokenServiceMock.Verify(service => service.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        _tokenServiceMock.Verify(service => service.GenerateRefreshToken(), Times.Never);
    }
}
