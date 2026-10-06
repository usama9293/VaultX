namespace PasswordManager.Domain.Entities;

public class Vault
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public User User { get; private set; } = default!;
    public ICollection<VaultEntry> Entries { get; private set; } = new List<VaultEntry>();

    private Vault()
    {
    }

    public Vault(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
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
