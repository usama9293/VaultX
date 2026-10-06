using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;

namespace PasswordManager.Application.Features.Vault.GetCurrent;

public sealed class GetCurrentVaultHandler : IGetCurrentVaultHandler
{
    private readonly ICurrentUser _currentUser;
    private readonly IVaultRepository _vaultRepository;

    public GetCurrentVaultHandler(ICurrentUser currentUser, IVaultRepository vaultRepository)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _vaultRepository = vaultRepository ?? throw new ArgumentNullException(nameof(vaultRepository));
    }

    public async Task<VaultResponse?> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidCurrentUserIdentityException();
        var vault = await _vaultRepository.GetByUserIdAsync(userId, cancellationToken);

        return vault is null
            ? null
            : new VaultResponse(
                vault.Id,
                new DateTimeOffset(DateTime.SpecifyKind(vault.CreatedAt, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(vault.UpdatedAt, DateTimeKind.Utc)));
    }
}
