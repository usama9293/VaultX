using PasswordManager.Application.DTOs.Vault;

namespace PasswordManager.Application.Features.Vault.Entries;

public interface IVaultEntryService
{
    Task<VaultEntryResponse?> CreateAsync(
        CreateVaultEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<VaultEntryPageResponse?> GetPageAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<VaultEntryResponse?> GetByIdAsync(Guid entryId, CancellationToken cancellationToken = default);

    Task<VaultEntryResponse?> UpdateAsync(
        Guid entryId,
        UpdateVaultEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid entryId, CancellationToken cancellationToken = default);
}
