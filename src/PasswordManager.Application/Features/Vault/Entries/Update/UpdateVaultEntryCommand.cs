namespace PasswordManager.Application.Features.Vault.Entries.Update;

public sealed record UpdateVaultEntryCommand(
    Guid Id,
    string Title,
    string WebsiteUrl,
    string Username,
    string? Notes,
    byte[] EncryptedPassword,
    byte[] PasswordNonce,
    byte[] PasswordAuthenticationTag);
