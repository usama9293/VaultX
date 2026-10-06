using Microsoft.EntityFrameworkCore;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.Infrastructure.Repositories;

public sealed class VaultRepository : IVaultRepository
{
    private readonly ApplicationDbContext _dbContext;

    public VaultRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<Vault?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Vaults
            .AsNoTracking()
            .SingleOrDefaultAsync(vault => vault.UserId == userId, cancellationToken);

    public async Task AddAsync(Vault vault, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vault);
        await _dbContext.Vaults.AddAsync(vault, cancellationToken);
    }

    public Task UpdateAsync(Vault vault, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _dbContext.Vaults.Update(vault);
        return Task.CompletedTask;
    }
}
