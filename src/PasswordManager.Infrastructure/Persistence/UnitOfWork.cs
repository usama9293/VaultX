using Microsoft.EntityFrameworkCore;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;

namespace PasswordManager.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;

    public UnitOfWork(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsEmailUniqueConstraintViolation(ex))
        {
            throw new DuplicateEmailException("A user with this email already exists.", ex);
        }
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static bool IsEmailUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;

        return message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("unique", StringComparison.OrdinalIgnoreCase) && message.Contains("email", StringComparison.OrdinalIgnoreCase));
    }
}
