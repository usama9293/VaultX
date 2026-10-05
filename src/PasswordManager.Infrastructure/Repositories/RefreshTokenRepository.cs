using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _dbContext;

    public RefreshTokenRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return null;
        }

        return await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);
    }

    public async Task LockUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var providerName = _dbContext.Database.ProviderName;
        if (providerName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            return;
        }

        if (providerName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            throw new NotSupportedException("Refresh-token user locking requires PostgreSQL.");
        }

        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A transaction is required before locking the token owner.");
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM \"Users\" WHERE \"Id\" = @userId FOR UPDATE";
        command.Transaction = transaction.GetDbTransaction();

        var userIdParameter = command.CreateParameter();
        userIdParameter.ParameterName = "userId";
        userIdParameter.DbType = DbType.Guid;
        userIdParameter.Value = userId;
        command.Parameters.Add(userIdParameter);

        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<List<RefreshToken>> GetByFamilyIdAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RefreshTokens
            .Where(refreshToken => refreshToken.FamilyId == familyId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.ExpiresAt > now)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        if (refreshToken is null)
        {
            throw new ArgumentNullException(nameof(refreshToken));
        }

        await _dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
    }

    public Task UpdateAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        _dbContext.RefreshTokens.Update(refreshToken);
        return Task.CompletedTask;
    }
}
