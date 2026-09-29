namespace PasswordManager.Domain.Entities;

public class VaultEntry
{
    public Guid Id { get; private set; }
    public Guid VaultId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? WebsiteUrl { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public byte[] EncryptedPassword { get; private set; } = Array.Empty<byte>();
    public byte[] PasswordNonce { get; private set; } = Array.Empty<byte>();
    public byte[] PasswordAuthenticationTag { get; private set; } = Array.Empty<byte>();
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Vault Vault { get; private set; } = default!;

    private VaultEntry()
    {
    }

    public VaultEntry(
        Guid vaultId,
        string title,
        string username,
        byte[] encryptedPassword,
        byte[] passwordNonce,
        byte[] passwordAuthenticationTag,
        string? websiteUrl = null,
        string? notes = null)
    {
        if (vaultId == Guid.Empty)
        {
            throw new ArgumentException("Vault id is required.", nameof(vaultId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (encryptedPassword is null || encryptedPassword.Length == 0)
        {
            throw new ArgumentException("Encrypted password is required.", nameof(encryptedPassword));
        }

        if (passwordNonce is null || passwordNonce.Length == 0)
        {
            throw new ArgumentException("Password nonce is required.", nameof(passwordNonce));
        }

        if (passwordAuthenticationTag is null || passwordAuthenticationTag.Length == 0)
        {
            throw new ArgumentException("Password authentication tag is required.", nameof(passwordAuthenticationTag));
        }

        Id = Guid.NewGuid();
        VaultId = vaultId;
        Title = title.Trim();
        Username = username.Trim();
        WebsiteUrl = string.IsNullOrWhiteSpace(websiteUrl) ? null : websiteUrl.Trim();
        EncryptedPassword = encryptedPassword.ToArray();
        PasswordNonce = passwordNonce.ToArray();
        PasswordAuthenticationTag = passwordAuthenticationTag.ToArray();
        Notes = notes;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void Update(
        string title,
        string username,
        byte[] encryptedPassword,
        byte[] passwordNonce,
        byte[] passwordAuthenticationTag,
        string? websiteUrl = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (encryptedPassword is null || encryptedPassword.Length == 0)
        {
            throw new ArgumentException("Encrypted password is required.", nameof(encryptedPassword));
        }

        if (passwordNonce is null || passwordNonce.Length == 0)
        {
            throw new ArgumentException("Password nonce is required.", nameof(passwordNonce));
        }

        if (passwordAuthenticationTag is null || passwordAuthenticationTag.Length == 0)
        {
            throw new ArgumentException("Password authentication tag is required.", nameof(passwordAuthenticationTag));
        }

        Title = title.Trim();
        Username = username.Trim();
        WebsiteUrl = string.IsNullOrWhiteSpace(websiteUrl) ? null : websiteUrl.Trim();
        EncryptedPassword = encryptedPassword.ToArray();
        PasswordNonce = passwordNonce.ToArray();
        PasswordAuthenticationTag = passwordAuthenticationTag.ToArray();
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }
}
