namespace PasswordManager.Application.Features.Vault.Initialize;

public interface IInitializeVaultHandler
{
    Task<InitializeVaultResult> HandleAsync(CancellationToken cancellationToken = default);
}
