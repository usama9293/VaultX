using System.Security.Cryptography;
using PasswordManager.Application.Interfaces.Security;

namespace PasswordManager.Infrastructure.Services;

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; // 128 bits
    private const int KeySize = 32; // 256 bits
    private const int DefaultIterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public Task<string> HashPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subKey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            Algorithm,
            KeySize);

        var hashString = $"$pbkdf2-sha256$i={DefaultIterations}$s={Convert.ToBase64String(salt)}$h={Convert.ToBase64String(subKey)}";
        return Task.FromResult(hashString);
    }

    public Task<bool> VerifyPasswordAsync(string password, string hashedPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hashedPassword))
        {
            return Task.FromResult(false);
        }

        try
        {
            var parts = hashedPassword.Split('$');
            if (parts.Length != 5 || parts[1] != "pbkdf2-sha256")
            {
                return Task.FromResult(false);
            }

            var iterationsPart = parts[2];
            var saltPart = parts[3];
            var hashPart = parts[4];

            if (!iterationsPart.StartsWith("i=") || !saltPart.StartsWith("s=") || !hashPart.StartsWith("h="))
            {
                return Task.FromResult(false);
            }

            if (!int.TryParse(iterationsPart["i=".Length..], out var iterations) || iterations <= 0)
            {
                return Task.FromResult(false);
            }

            var salt = Convert.FromBase64String(saltPart["s=".Length..]);
            var expectedKey = Convert.FromBase64String(hashPart["h=".Length..]);

            var computedKey = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                Algorithm,
                expectedKey.Length);

            var isValid = CryptographicOperations.FixedTimeEquals(computedKey, expectedKey);
            return Task.FromResult(isValid);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
