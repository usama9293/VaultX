namespace PasswordManager.Application.DTOs.Vault;

/// <summary>Replacement metadata for an existing vault entry.</summary>
public sealed record UpdateVaultEntryRequest
{
    /// <summary>Entry label, limited to 255 Unicode scalar values.</summary>
    public required string Title { get; init; }

    /// <summary>Optional HTTP or HTTPS website address.</summary>
    public string? WebsiteUrl { get; init; }

    /// <summary>Login name or account identifier.</summary>
    public required string Username { get; init; }

    /// <summary>Optional descriptive notes. Passwords are not accepted or stored.</summary>
    public string? Notes { get; init; }
}
