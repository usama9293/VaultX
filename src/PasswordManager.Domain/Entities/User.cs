namespace PasswordManager.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public byte[] PasswordHash { get; private set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Vault? Vault { get; private set; }
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    private User()
    {
    }

    public User(string email, byte[] passwordHash)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (passwordHash is null || passwordHash.Length == 0)
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        Id = Guid.NewGuid();
        Email = email.Trim();
        PasswordHash = passwordHash.ToArray();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        Email = email.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePasswordHash(byte[] passwordHash)
    {
        if (passwordHash is null || passwordHash.Length == 0)
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash.ToArray();
        UpdatedAt = DateTime.UtcNow;
    }
}
