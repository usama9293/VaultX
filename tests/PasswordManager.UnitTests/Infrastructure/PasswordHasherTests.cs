using PasswordManager.Infrastructure.Services;
using Xunit;

namespace PasswordManager.UnitTests.Infrastructure;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public async Task HashPasswordAsync_ValidPassword_ReturnsFormattedHashString()
    {
        var password = "MySecretPassword123!";
        var hash = await _hasher.HashPasswordAsync(password);

        Assert.NotNull(hash);
        Assert.StartsWith("$pbkdf2-sha256$", hash);
        Assert.Contains("$i=", hash);
        Assert.Contains("$s=", hash);
        Assert.Contains("$h=", hash);
    }

    [Fact]
    public async Task HashPasswordAsync_SamePasswordTwice_GeneratesDifferentHashesDueToSalt()
    {
        var password = "MySecretPassword123!";
        var hash1 = await _hasher.HashPasswordAsync(password);
        var hash2 = await _hasher.HashPasswordAsync(password);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task VerifyPasswordAsync_MatchingPassword_ReturnsTrue()
    {
        var password = "MySecretPassword123!";
        var hash = await _hasher.HashPasswordAsync(password);

        var result = await _hasher.VerifyPasswordAsync(password, hash);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyPasswordAsync_IncorrectPassword_ReturnsFalse()
    {
        var password = "MySecretPassword123!";
        var hash = await _hasher.HashPasswordAsync(password);

        var result = await _hasher.VerifyPasswordAsync("WrongPassword999!", hash);

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task VerifyPasswordAsync_EmptyInputs_ReturnsFalse(string? input)
    {
        var hash = await _hasher.HashPasswordAsync("ValidPassword123!");

        Assert.False(await _hasher.VerifyPasswordAsync(input!, hash));
        Assert.False(await _hasher.VerifyPasswordAsync("ValidPassword123!", input!));
    }

    [Fact]
    public async Task VerifyPasswordAsync_MalformedHash_ReturnsFalse()
    {
        var result = await _hasher.VerifyPasswordAsync("ValidPassword123!", "not_a_valid_hash");
        Assert.False(result);
    }
}
