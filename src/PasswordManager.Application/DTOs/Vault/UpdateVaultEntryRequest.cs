namespace PasswordManager.Application.DTOs.Vault;

public sealed record UpdateVaultEntryRequest(
    Guid Id,
    string Title,
    string WebsiteUrl,
    string Username,
    string? Notes,
    byte[] EncryptedPassword,
    byte[] PasswordNonce,
    byte[] PasswordAuthenticationTag);
