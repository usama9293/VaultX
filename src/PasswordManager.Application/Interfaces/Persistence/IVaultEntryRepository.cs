using PasswordManager.Domain.Entities;
using PasswordManager.Application.DTOs.Vault;

namespace PasswordManager.Application.Interfaces.Persistence;

public interface IVaultEntryRepository
{
    Task<VaultEntryData?> GetByIdAndVaultIdAsync(
        Guid id,
        Guid vaultId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VaultEntryListItemData>> GetPageByVaultIdAsync(
        Guid vaultId,
        string? query,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
    Task AddAsync(VaultEntry entry, CancellationToken cancellationToken = default);
    Task<bool> UpdateByIdAndVaultIdAsync(
        Guid id,
        Guid vaultId,
        string title,
        string? websiteUrl,
        string username,
        string? notes,
        DateTime updatedAt,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteByIdAndVaultIdAsync(
        Guid id,
        Guid vaultId,
        CancellationToken cancellationToken = default);
}
