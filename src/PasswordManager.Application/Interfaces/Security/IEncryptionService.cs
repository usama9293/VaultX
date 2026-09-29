namespace PasswordManager.Application.Interfaces.Security;

public interface IEncryptionService
{
    Task<byte[]> EncryptAsync(byte[] plaintext, byte[] key, byte[] nonce, CancellationToken cancellationToken = default);
    Task<byte[]> DecryptAsync(byte[] cipherText, byte[] key, byte[] nonce, byte[] authenticationTag, CancellationToken cancellationToken = default);
}
