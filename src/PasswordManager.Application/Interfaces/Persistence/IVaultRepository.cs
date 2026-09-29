using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Interfaces.Persistence;

public interface IVaultRepository
{
    Task<Vault?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(Vault vault, CancellationToken cancellationToken = default);
    Task UpdateAsync(Vault vault, CancellationToken cancellationToken = default);
}
