using Moq;
using PasswordManager.Application.Features.Users.GetById;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;

namespace PasswordManager.UnitTests.Application;

public class GetUserByIdHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly GetUserByIdHandler _handler;

    public GetUserByIdHandlerTests()
    {
        _handler = new GetUserByIdHandler(_currentUserMock.Object, _userRepositoryMock.Object);
    }

    [Fact]
    public async Task HandleAsync_OwnerAccess_ReturnsSafeUserResponse()
    {
        var user = CreateUser();
        _currentUserMock.Setup(currentUser => currentUser.UserId).Returns(user.Id);
        _userRepositoryMock
            .Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync(new GetUserByIdQuery(user.Id));

        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.CreatedAt, result.CreatedAt);
        Assert.Equal(user.UpdatedAt, result.UpdatedAt);
        _userRepositoryMock.Verify(
            repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CrossUserRequest_ReturnsNotFoundWithoutLoadingRequestedUser()
    {
        var currentUserId = Guid.NewGuid();
        var requestedUserId = Guid.NewGuid();
        _currentUserMock.Setup(currentUser => currentUser.UserId).Returns(currentUserId);

        var result = await _handler.HandleAsync(new GetUserByIdQuery(requestedUserId));

        Assert.Null(result);
        _userRepositoryMock.Verify(
            repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingCurrentUser_ReturnsNotFoundWithoutLoadingUser()
    {
        _currentUserMock.Setup(currentUser => currentUser.UserId).Returns((Guid?)null);

        var result = await _handler.HandleAsync(new GetUserByIdQuery(Guid.NewGuid()));

        Assert.Null(result);
        _userRepositoryMock.Verify(
            repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RequestedUserDoesNotExist_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        _currentUserMock.Setup(currentUser => currentUser.UserId).Returns(userId);
        _userRepositoryMock
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _handler.HandleAsync(new GetUserByIdQuery(userId));

        Assert.Null(result);
        _userRepositoryMock.Verify(
            repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static User CreateUser()
    {
        return new User("owner@example.com", [1, 2, 3]);
    }
}
