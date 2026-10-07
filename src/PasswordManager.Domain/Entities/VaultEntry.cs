namespace PasswordManager.Domain.Entities;

public class VaultEntry
{
    public Guid Id { get; private set; }
    public Guid VaultId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? WebsiteUrl { get; private set; }
    public string Username { get; private set; } = string.Empty;
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

        Id = Guid.NewGuid();
        VaultId = vaultId;
        Title = title;
        Username = username;
        WebsiteUrl = websiteUrl;
        Notes = notes;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void Update(string title, string username, string? websiteUrl, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        Title = title;
        Username = username;
        WebsiteUrl = websiteUrl;
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }
}
