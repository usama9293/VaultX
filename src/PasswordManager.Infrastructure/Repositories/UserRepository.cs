using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
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
            throw new NotSupportedException("Account lockout updates require PostgreSQL.");
        }

        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A transaction is required before locking a user.");
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM \"Users\" WHERE \"Id\" = @userId FOR UPDATE";
        command.Transaction = transaction.GetDbTransaction();

        var parameter = command.CreateParameter();
        parameter.ParameterName = "userId";
        parameter.DbType = DbType.Guid;
        parameter.Value = userId;
        command.Parameters.Add(parameter);

        await command.ExecuteScalarAsync(cancellationToken);
    }

    public Task<User?> GetFreshByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        await _dbContext.Users.AddAsync(user, cancellationToken);
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        _dbContext.Users.Update(user);
        return Task.CompletedTask;
    }
}
