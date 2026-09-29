using Microsoft.EntityFrameworkCore;
using PasswordManager.Application.Exceptions;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;
using Xunit;

namespace PasswordManager.UnitTests.Infrastructure;

public class UnitOfWorkTests
{
    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_NormalOperation_ReturnsCommittedCount()
    {
        using var context = CreateDbContext();
        var uow = new UnitOfWork(context);

        context.Users.Add(new User("unique@example.com", new byte[] { 1, 2, 3 }));
        var result = await uow.SaveChangesAsync();

        Assert.True(result > 0);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenUniqueConstraintViolated_ThrowsDuplicateEmailException()
    {
        // Simulate DbUpdateException with unique index error
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var contextMock = new ThrowingDbContext(options, new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new Exception("duplicate key value violates unique constraint \"IX_Users_Email\"")));

        var uow = new UnitOfWork(contextMock);

        var ex = await Assert.ThrowsAsync<DuplicateEmailException>(() => uow.SaveChangesAsync());
        Assert.Equal("A user with this email already exists.", ex.Message);
    }

    private class ThrowingDbContext : ApplicationDbContext
    {
        private readonly DbUpdateException _exceptionToThrow;

        public ThrowingDbContext(DbContextOptions<ApplicationDbContext> options, DbUpdateException exceptionToThrow)
            : base(options)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw _exceptionToThrow;
        }
    }
}
