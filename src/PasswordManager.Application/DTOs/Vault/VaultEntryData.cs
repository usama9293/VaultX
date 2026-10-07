namespace PasswordManager.Application.DTOs.Vault;

public sealed record VaultEntryData(
    Guid Id,
    string Title,
    string? WebsiteUrl,
    string Username,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record VaultEntryListItemData(
    Guid Id,
    string Title,
    string? WebsiteUrl,
    string Username,
    DateTime CreatedAt,
    DateTime UpdatedAt);
