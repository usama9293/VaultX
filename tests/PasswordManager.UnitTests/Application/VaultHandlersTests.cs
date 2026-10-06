using Moq;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Vault.GetCurrent;
using PasswordManager.Application.Features.Vault.Initialize;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using VaultEntity = PasswordManager.Domain.Entities.Vault;

namespace PasswordManager.UnitTests.Application;

public class VaultHandlersTests
{
    [Fact]
    public async Task InitializeVault_CreatesVaultForCurrentUser()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(value => value.UserId).Returns(userId);
        repository.Setup(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntity?)null);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await new InitializeVaultHandler(
            currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync();

        Assert.True(result.Created);
        Assert.NotEqual(Guid.Empty, result.Vault.Id);
        Assert.Equal(DateTimeOffset.UtcNow.Offset, result.Vault.CreatedAt.Offset);
        repository.Verify(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.AddAsync(
            It.Is<VaultEntity>(vault => vault.UserId == userId),
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeVault_UsesOnlyCurrentUserAsOwner()
    {
        var authenticatedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(value => value.UserId).Returns(authenticatedUserId);
        repository.Setup(value => value.GetByUserIdAsync(authenticatedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntity?)null);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await new InitializeVaultHandler(
            currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync();

        repository.Verify(value => value.GetByUserIdAsync(otherUserId, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.AddAsync(
            It.Is<VaultEntity>(vault => vault.UserId == authenticatedUserId),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotEqual(otherUserId, authenticatedUserId);
        Assert.True(result.Created);
    }

    [Fact]
    public async Task InitializeVault_MissingIdentityIsRejectedWithoutPersistence()
    {
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(value => value.UserId).Returns((Guid?)null);

        await Assert.ThrowsAsync<InvalidCurrentUserIdentityException>(() =>
            new InitializeVaultHandler(currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync());

        repository.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializeVault_MalformedIdentityIsRejectedWithoutPersistence()
    {
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(value => value.UserId).Throws<InvalidCurrentUserIdentityException>();

        await Assert.ThrowsAsync<InvalidCurrentUserIdentityException>(() =>
            new InitializeVaultHandler(currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync());

        repository.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializeVault_ExistingVaultReturnsItWithoutSaving()
    {
        var userId = Guid.NewGuid();
        var vault = new VaultEntity(userId);
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(value => value.UserId).Returns(userId);
        repository.Setup(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vault);

        var result = await new InitializeVaultHandler(
            currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync();

        Assert.False(result.Created);
        Assert.Equal(vault.Id, result.Vault.Id);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(vault.CreatedAt, DateTimeKind.Utc)), result.Vault.CreatedAt);
        repository.Verify(value => value.AddAsync(It.IsAny<VaultEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitializeVault_ExpectedUniqueOwnerConflictReturnsConcurrentVault()
    {
        var userId = Guid.NewGuid();
        var concurrentlyCreatedVault = new VaultEntity(userId);
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(value => value.UserId).Returns(userId);
        repository.SetupSequence(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntity?)null)
            .ReturnsAsync(concurrentlyCreatedVault);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateVaultException("duplicate owner", new InvalidOperationException()));

        var result = await new InitializeVaultHandler(
            currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync();

        Assert.False(result.Created);
        Assert.Equal(concurrentlyCreatedVault.Id, result.Vault.Id);
        repository.Verify(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task InitializeVault_UnrelatedPersistenceFailureIsNotSwallowed()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var failure = new InvalidOperationException("database unavailable");
        currentUser.Setup(value => value.UserId).Returns(userId);
        repository.Setup(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntity?)null);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InitializeVaultHandler(currentUser.Object, repository.Object, unitOfWork.Object).HandleAsync());

        Assert.Same(failure, thrown);
        repository.Verify(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrentVault_MapsOnlyApprovedResponseFields()
    {
        var userId = Guid.NewGuid();
        var vault = new VaultEntity(userId);
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        currentUser.Setup(value => value.UserId).Returns(userId);
        repository.Setup(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vault);

        var response = await new GetCurrentVaultHandler(currentUser.Object, repository.Object).HandleAsync();

        Assert.NotNull(response);
        Assert.Equal(vault.Id, response.Id);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(vault.CreatedAt, DateTimeKind.Utc)), response.CreatedAt);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(vault.UpdatedAt, DateTimeKind.Utc)), response.UpdatedAt);
        Assert.Equal(new[] { "CreatedAt", "Id", "UpdatedAt" },
            response.GetType().GetProperties().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public async Task GetCurrentVault_MissingVaultReturnsNullWithoutCreation()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        currentUser.Setup(value => value.UserId).Returns(userId);
        repository.Setup(value => value.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntity?)null);

        var response = await new GetCurrentVaultHandler(currentUser.Object, repository.Object).HandleAsync();

        Assert.Null(response);
        repository.Verify(value => value.AddAsync(It.IsAny<VaultEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentVault_MissingIdentityIsRejected()
    {
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IVaultRepository>();
        currentUser.Setup(value => value.UserId).Returns((Guid?)null);

        await Assert.ThrowsAsync<InvalidCurrentUserIdentityException>(() =>
            new GetCurrentVaultHandler(currentUser.Object, repository.Object).HandleAsync());

        repository.VerifyNoOtherCalls();
    }
}
