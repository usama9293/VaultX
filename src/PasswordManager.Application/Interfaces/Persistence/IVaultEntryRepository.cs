using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Interfaces.Persistence;

public interface IVaultEntryRepository
{
    Task<VaultEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VaultEntry>> GetByVaultIdAsync(Guid vaultId, CancellationToken cancellationToken = default);
    Task AddAsync(VaultEntry entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(VaultEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
