namespace PasswordManager.Domain.Entities;

public class Vault
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public byte[] EncryptedKey { get; private set; } = Array.Empty<byte>();
    public byte[] KeyNonce { get; private set; } = Array.Empty<byte>();
    public byte[] KeyAuthenticationTag { get; private set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public User User { get; private set; } = default!;
    public ICollection<VaultEntry> Entries { get; private set; } = new List<VaultEntry>();

    private Vault()
    {
    }

    public Vault(Guid userId, byte[] encryptedKey, byte[] keyNonce, byte[] keyAuthenticationTag)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (encryptedKey is null || encryptedKey.Length == 0)
        {
            throw new ArgumentException("Encrypted key is required.", nameof(encryptedKey));
        }

        if (keyNonce is null || keyNonce.Length == 0)
        {
            throw new ArgumentException("Key nonce is required.", nameof(keyNonce));
        }

        if (keyAuthenticationTag is null || keyAuthenticationTag.Length == 0)
        {
            throw new ArgumentException("Key authentication tag is required.", nameof(keyAuthenticationTag));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        EncryptedKey = encryptedKey.ToArray();
        KeyNonce = keyNonce.ToArray();
        KeyAuthenticationTag = keyAuthenticationTag.ToArray();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateEncryptedKey(byte[] encryptedKey, byte[] keyNonce, byte[] keyAuthenticationTag)
    {
        if (encryptedKey is null || encryptedKey.Length == 0)
        {
            throw new ArgumentException("Encrypted key is required.", nameof(encryptedKey));
        }

        if (keyNonce is null || keyNonce.Length == 0)
        {
            throw new ArgumentException("Key nonce is required.", nameof(keyNonce));
        }

        if (keyAuthenticationTag is null || keyAuthenticationTag.Length == 0)
        {
            throw new ArgumentException("Key authentication tag is required.", nameof(keyAuthenticationTag));
        }

        EncryptedKey = encryptedKey.ToArray();
        KeyNonce = keyNonce.ToArray();
        KeyAuthenticationTag = keyAuthenticationTag.ToArray();
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddEntry(VaultEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (Entries.Any(existing => existing.Id == entry.Id))
        {
            return;
        }

        Entries.Add(entry);
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveEntry(Guid entryId)
    {
        var entry = Entries.FirstOrDefault(x => x.Id == entryId);

        if (entry is not null)
        {
            Entries.Remove(entry);
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
