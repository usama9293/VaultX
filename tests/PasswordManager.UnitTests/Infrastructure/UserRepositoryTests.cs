using Microsoft.EntityFrameworkCore;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;
using PasswordManager.Infrastructure.Repositories;
using Xunit;

namespace PasswordManager.UnitTests.Infrastructure;

public class UserRepositoryTests
{
    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_SuccessfullyPersistsAndRetrievesUser()
    {
        using var context = CreateDbContext();
        var repository = new UserRepository(context);

        var user = new User("test@example.com", new byte[] { 10, 20, 30 });

        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(user.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(user.Id, retrieved!.Id);
        Assert.Equal("test@example.com", retrieved.Email);
    }

    [Fact]
    public async Task GetByEmailAsync_FindsUserCaseInsensitively()
    {
        using var context = CreateDbContext();
        var repository = new UserRepository(context);

        var user = new User("test@example.com", new byte[] { 10, 20, 30 });
        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByEmailAsync("TEST@EXAMPLE.COM");

        Assert.NotNull(retrieved);
        Assert.Equal(user.Id, retrieved!.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_NonExistentEmail_ReturnsNull()
    {
        using var context = CreateDbContext();
        var repository = new UserRepository(context);

        var retrieved = await repository.GetByEmailAsync("nonexistent@example.com");

        Assert.Null(retrieved);
    }
}
