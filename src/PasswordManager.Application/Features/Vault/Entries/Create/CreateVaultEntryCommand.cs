namespace PasswordManager.Application.Features.Vault.Entries.Create;

public sealed record CreateVaultEntryCommand(
    Guid VaultId,
    string Title,
    string WebsiteUrl,
    string Username,
    string? Notes,
    byte[] EncryptedPassword,
    byte[] PasswordNonce,
    byte[] PasswordAuthenticationTag);
