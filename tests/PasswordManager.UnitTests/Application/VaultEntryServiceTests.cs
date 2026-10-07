using System.Text.Json;
using Moq;
using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Vault.Entries;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using VaultEntity = PasswordManager.Domain.Entities.Vault;

namespace PasswordManager.UnitTests.Application;

public sealed class VaultEntryServiceTests
{
    [Fact]
    public async Task Create_UsesCurrentUsersVaultAndStoresNormalizedMetadataOnly()
    {
        var userId = Guid.NewGuid();
        var vault = new VaultEntity(userId);
        var currentUser = new Mock<ICurrentUser>();
        var vaultRepository = new Mock<IVaultRepository>();
        var entryRepository = new Mock<IVaultEntryRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        vaultRepository.Setup(repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vault);
        unitOfWork.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        VaultEntry? addedEntry = null;
        entryRepository.Setup(repository => repository.AddAsync(
                It.IsAny<VaultEntry>(),
                It.IsAny<CancellationToken>()))
            .Callback<VaultEntry, CancellationToken>((entry, _) => addedEntry = entry)
            .Returns(Task.CompletedTask);

        var service = new VaultEntryService(
            currentUser.Object, vaultRepository.Object, entryRepository.Object, unitOfWork.Object);
        var result = await service.CreateAsync(new CreateVaultEntryRequest
        {
            Title = "  Cafe\u0301 account  ",
            Username = " user@example.test ",
            WebsiteUrl = "  ",
            Notes = "Useful note"
        });

        Assert.NotNull(addedEntry);
        Assert.Equal(vault.Id, addedEntry.VaultId);
        Assert.Equal("Café account", addedEntry.Title);
        Assert.Equal("user@example.test", addedEntry.Username);
        Assert.Null(addedEntry.WebsiteUrl);
        Assert.Equal("Useful note", addedEntry.Notes);
        Assert.NotNull(result);
        Assert.Equal("Café account", result.Title);
        Assert.Null(result.WebsiteUrl);
        Assert.DoesNotContain("VaultId", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("Password", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        vaultRepository.Verify(
            repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        entryRepository.Verify(
            repository => repository.AddAsync(
                It.Is<VaultEntry>(entry => entry.VaultId == vault.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAndDelete_AreScopedToTheCurrentUsersVault()
    {
        var userId = Guid.NewGuid();
        var vault = new VaultEntity(userId);
        var entryId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var vaultRepository = new Mock<IVaultRepository>();
        var entryRepository = new Mock<IVaultEntryRepository>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        vaultRepository.Setup(repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vault);
        entryRepository.Setup(repository => repository.GetByIdAndVaultIdAsync(
                entryId, vault.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntryData?)null);
        entryRepository.Setup(repository => repository.DeleteByIdAndVaultIdAsync(
                entryId, vault.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new VaultEntryService(
            currentUser.Object,
            vaultRepository.Object,
            entryRepository.Object,
            Mock.Of<IUnitOfWork>());

        Assert.Null(await service.GetByIdAsync(entryId));
        Assert.False(await service.DeleteAsync(entryId));
        entryRepository.Verify(repository => repository.GetByIdAndVaultIdAsync(
            entryId, vault.Id, It.IsAny<CancellationToken>()), Times.Once);
        entryRepository.Verify(repository => repository.DeleteByIdAndVaultIdAsync(
            entryId, vault.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_AppliesMetadataThroughOwnerScopedPredicate()
    {
        var userId = Guid.NewGuid();
        var vault = new VaultEntity(userId);
        var entryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var currentUser = new Mock<ICurrentUser>();
        var vaultRepository = new Mock<IVaultRepository>();
        var entryRepository = new Mock<IVaultEntryRepository>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        vaultRepository.Setup(repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vault);
        entryRepository.Setup(repository => repository.UpdateByIdAndVaultIdAsync(
                entryId,
                vault.Id,
                "Updated title",
                null,
                "updated-user",
                "updated notes",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        entryRepository.Setup(repository => repository.GetByIdAndVaultIdAsync(
                entryId, vault.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VaultEntryData(
                entryId, "Updated title", null, "updated-user", "updated notes", now, now));
        var service = new VaultEntryService(
            currentUser.Object,
            vaultRepository.Object,
            entryRepository.Object,
            Mock.Of<IUnitOfWork>());

        var result = await service.UpdateAsync(entryId, new UpdateVaultEntryRequest
        {
            Title = "Updated title",
            Username = "updated-user",
            Notes = "updated notes"
        });

        Assert.NotNull(result);
        Assert.Equal("Updated title", result.Title);
        Assert.Null(result.WebsiteUrl);
        entryRepository.Verify(repository => repository.UpdateByIdAndVaultIdAsync(
            entryId,
            vault.Id,
            "Updated title",
            null,
            "updated-user",
            "updated notes",
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_NormalizesSearchAndMapsOnlyListFieldsWithHasMore()
    {
        var userId = Guid.NewGuid();
        var vault = new VaultEntity(userId);
        var createdAt = DateTime.UtcNow;
        var currentUser = new Mock<ICurrentUser>();
        var vaultRepository = new Mock<IVaultRepository>();
        var entryRepository = new Mock<IVaultEntryRepository>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        vaultRepository.Setup(repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vault);
        entryRepository.Setup(repository => repository.GetPageByVaultIdAsync(
                vault.Id, "Café", 20, 21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 21)
                .Select(index => new VaultEntryListItemData(
                    Guid.NewGuid(),
                    $"Entry {index}",
                    null,
                    "user",
                    createdAt,
                    createdAt))
                .ToArray());
        var service = new VaultEntryService(
            currentUser.Object,
            vaultRepository.Object,
            entryRepository.Object,
            Mock.Of<IUnitOfWork>());

        var result = await service.GetPageAsync(" Cafe\u0301 ", page: 2, pageSize: 20);

        Assert.NotNull(result);
        Assert.Equal(2, result.Page);
        Assert.Equal(20, result.Items.Count);
        Assert.True(result.HasMore);
        Assert.DoesNotContain("Notes", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        entryRepository.Verify(repository => repository.GetPageByVaultIdAsync(
            vault.Id, "Café", 20, 21, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_RejectsUnsafeOrOversizedMetadataWithoutSaving()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var vaultRepository = new Mock<IVaultRepository>();
        var entryRepository = new Mock<IVaultEntryRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        vaultRepository.Setup(repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VaultEntity(userId));
        var service = new VaultEntryService(
            currentUser.Object, vaultRepository.Object, entryRepository.Object, unitOfWork.Object);

        var invalidUrl = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateVaultEntryRequest
            {
                Title = "Valid title",
                Username = "valid-user",
                WebsiteUrl = "javascript:alert(1)"
            }));
        Assert.Contains(nameof(CreateVaultEntryRequest.WebsiteUrl), invalidUrl.Errors.Keys);

        var oversizedTitle = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateVaultEntryRequest
            {
                Title = new string('a', 256),
                Username = "valid-user"
            }));
        Assert.Contains(nameof(CreateVaultEntryRequest.Title), oversizedTitle.Errors.Keys);
        entryRepository.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingVaultAndInvalidIdentityNeverCreateOrQueryEntries()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var vaultRepository = new Mock<IVaultRepository>();
        var entryRepository = new Mock<IVaultEntryRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        vaultRepository.Setup(repository => repository.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaultEntity?)null);
        var service = new VaultEntryService(
            currentUser.Object, vaultRepository.Object, entryRepository.Object, unitOfWork.Object);

        Assert.Null(await service.CreateAsync(new CreateVaultEntryRequest
        {
            Title = "Title",
            Username = "user"
        }));
        Assert.Null(await service.GetPageAsync(null, 1, 20));
        entryRepository.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();

        currentUser.SetupGet(user => user.UserId).Returns((Guid?)null);
        await Assert.ThrowsAsync<InvalidCurrentUserIdentityException>(() => service.GetPageAsync(null, 1, 20));
        entryRepository.VerifyNoOtherCalls();
    }
}
