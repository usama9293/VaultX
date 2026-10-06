using PasswordManager.Application.DTOs.Vault;

namespace PasswordManager.Application.Features.Vault.Initialize;

public sealed record InitializeVaultResult(VaultResponse Vault, bool Created);
