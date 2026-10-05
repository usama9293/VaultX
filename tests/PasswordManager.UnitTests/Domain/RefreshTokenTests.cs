using PasswordManager.Domain.Entities;
using Xunit;

namespace PasswordManager.UnitTests.Domain;

public class RefreshTokenTests
{
    [Fact]
    public void Constructor_ValidParameters_InstantiatesActiveRefreshToken()
    {
        var userId = Guid.NewGuid();
        const string tokenHash = "sampletokenhash123456";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        var token = new RefreshToken(userId, tokenHash, expiresAt);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(userId, token.UserId);
        Assert.NotEqual(Guid.Empty, token.FamilyId);
        Assert.Equal(tokenHash, token.TokenHash);
        Assert.Equal(expiresAt, token.ExpiresAt);
        Assert.True(token.CreatedAt <= DateTime.UtcNow);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive);
        Assert.False(token.IsRevoked);
        Assert.False(token.IsExpired);
    }

    [Fact]
    public void Constructor_ExplicitFamilyId_AssignsFamilyToRefreshToken()
    {
        var familyId = Guid.NewGuid();

        var token = new RefreshToken(
            Guid.NewGuid(),
            familyId,
            "sampletokenhash123456",
            DateTime.UtcNow.AddDays(7));

        Assert.Equal(familyId, token.FamilyId);
    }

    [Fact]
    public void Constructor_EmptyFamilyId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(
                Guid.NewGuid(),
                Guid.Empty,
                "sampletokenhash123456",
                DateTime.UtcNow.AddDays(7)));
    }

    [Fact]
    public void Constructor_EmptyUserId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.Empty, "hash", DateTime.UtcNow.AddDays(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_EmptyTokenHash_ThrowsArgumentException(string? hash)
    {
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), hash!, DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void Constructor_PastExpiration_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public void IsExpired_WhenPastExpirationDate_ReturnsTrue()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMilliseconds(50));
        Thread.Sleep(60);

        Assert.True(token.IsExpired);
        Assert.False(token.IsActive);
    }

    [Fact]
    public void Revoke_SetsRevocationProperties()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(7));
        var replacedById = Guid.NewGuid();

        token.Revoke(replacedById);

        Assert.True(token.IsRevoked);
        Assert.False(token.IsActive);
        Assert.NotNull(token.RevokedAt);
        Assert.Equal(replacedById, token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_WhenAlreadyRevoked_DoesNotOverwriteOriginalRevocationTimestamp()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(7));
        token.Revoke();
        var initialRevokedAt = token.RevokedAt;

        Thread.Sleep(20);
        token.Revoke(Guid.NewGuid());

        Assert.Equal(initialRevokedAt, token.RevokedAt);
    }
}
