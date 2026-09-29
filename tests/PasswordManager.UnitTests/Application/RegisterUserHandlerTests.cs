using System.Text;
using Moq;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Authentication.Register;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Application.Interfaces.Security;
using PasswordManager.Domain.Entities;
using Xunit;

namespace PasswordManager.UnitTests.Application;

public class RegisterUserHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly RegisterUserHandler _handler;

    public RegisterUserHandlerTests()
    {
        _handler = new RegisterUserHandler(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_SuccessfullyRegistersUser()
    {
        // Arrange
        const string rawPassword = "StrongPassword123!";
        var command = new RegisterUserCommand("  TestUser@Example.COM  ", rawPassword, rawPassword);
        const string expectedHashedPassword = "$pbkdf2-sha256$i=100000$s=randomsalt$h=derivedhash";

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _passwordHasherMock
            .Setup(h => h.HashPasswordAsync(rawPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHashedPassword);

        User? capturedUser = null;
        _userRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => capturedUser = u)
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await _handler.HandleAsync(command);

        // Assert
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("testuser@example.com", response.Email);
        Assert.True(response.CreatedAt <= DateTime.UtcNow);

        // Verify captured user entity in persistence
        Assert.NotNull(capturedUser);
        Assert.Equal("testuser@example.com", capturedUser!.Email);
        Assert.Equal(expectedHashedPassword, Encoding.UTF8.GetString(capturedUser.PasswordHash));

        // Security check: Raw password is never stored
        var rawPasswordBytes = Encoding.UTF8.GetBytes(rawPassword);
        Assert.False(capturedUser.PasswordHash.SequenceEqual(rawPasswordBytes));

        // Verify interactions
        _userRepositoryMock.Verify(r => r.GetByEmailAsync("testuser@example.com", It.IsAny<CancellationToken>()), Times.Once);
        _passwordHasherMock.Verify(h => h.HashPasswordAsync(rawPassword, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DuplicateEmail_ThrowsDuplicateEmailException()
    {
        // Arrange
        const string rawPassword = "StrongPassword123!";
        var command = new RegisterUserCommand("existing@example.com", rawPassword, rawPassword);
        var existingUser = new User("existing@example.com", new byte[] { 1, 2, 3 });

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DuplicateEmailException>(() => _handler.HandleAsync(command));
        Assert.Equal("A user with this email already exists.", ex.Message);

        _passwordHasherMock.Verify(h => h.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _userRepositoryMock.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.HandleAsync(null!));
    }
}
