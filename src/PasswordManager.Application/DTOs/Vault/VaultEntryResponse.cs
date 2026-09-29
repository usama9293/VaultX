namespace PasswordManager.Application.DTOs.Vault;

public sealed record VaultEntryResponse(
    Guid Id,
    Guid VaultId,
    string Title,
    string WebsiteUrl,
    string Username,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);
