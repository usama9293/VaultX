using Microsoft.EntityFrameworkCore;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;
using Npgsql;

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
        catch (DbUpdateException ex) when (IsVaultUserUniqueConstraintViolation(ex))
        {
            throw new DuplicateVaultException("A vault already exists for this user.", ex);
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

    private static bool IsEmailUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException switch
        {
            PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_Users_Email"
            } => true,
            _ => (ex.InnerException?.Message ?? ex.Message)
                .Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase)
                || (ex.InnerException?.Message ?? ex.Message)
                    .Contains("Users.Email", StringComparison.OrdinalIgnoreCase)
        };

    private static bool IsVaultUserUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Vaults_UserId"
        };
}
