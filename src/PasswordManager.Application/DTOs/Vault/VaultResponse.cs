namespace PasswordManager.Application.DTOs.Vault;

/// <summary>Represents metadata for the authenticated user's vault.</summary>
public sealed record VaultResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
