namespace PasswordManager.Application.DTOs.Vault;

/// <summary>Entry metadata returned in a vault list without free-text notes.</summary>
public sealed record VaultEntryListItemResponse(
    Guid Id,
    string Title,
    string? WebsiteUrl,
    string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
