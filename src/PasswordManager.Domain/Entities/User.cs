namespace PasswordManager.Domain.Entities;

public class User
{
    public const int LoginFailureThreshold = 5;
    public static readonly TimeSpan LoginLockoutDuration = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public byte[] PasswordHash { get; private set; } = Array.Empty<byte>();
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
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

    public bool IsLocked(DateTime utcNow)
    {
        EnsureUtc(utcNow);
        return LockedUntil is not null && LockedUntil > utcNow;
    }

    public void RecordFailedLogin(DateTime utcNow)
    {
        EnsureUtc(utcNow);

        if (IsLocked(utcNow))
        {
            return;
        }

        ClearExpiredLockout(utcNow);
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= LoginFailureThreshold)
        {
            LockedUntil = utcNow.Add(LoginLockoutDuration);
        }
    }

    public void ClearExpiredLockout(DateTime utcNow)
    {
        EnsureUtc(utcNow);
        if (LockedUntil is not null && LockedUntil <= utcNow)
        {
            ResetLoginFailures();
        }
    }

    public void ResetLoginFailures()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.", nameof(value));
        }
    }
}
