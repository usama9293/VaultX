using PasswordManager.Application.DTOs.Vault;

namespace PasswordManager.Application.Features.Vault.GetCurrent;

public interface IGetCurrentVaultHandler
{
    Task<VaultResponse?> HandleAsync(CancellationToken cancellationToken = default);
}
