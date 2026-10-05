using System.Security.Cryptography;

namespace PasswordManager.IntegrationTests;

internal static class TestJwtSettings
{
    public static readonly string SecretKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
