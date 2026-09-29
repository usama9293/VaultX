namespace PasswordManager.Application.Interfaces.Security;

public interface IKeyDerivationService
{
    Task<byte[]> DeriveKeyAsync(string secret, byte[] salt, int keySizeBytes, CancellationToken cancellationToken = default);
}
