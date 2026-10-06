using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using VaultEntity = PasswordManager.Domain.Entities.Vault;

namespace PasswordManager.Application.Features.Vault.Initialize;

public sealed class InitializeVaultHandler : IInitializeVaultHandler
{
    private readonly ICurrentUser _currentUser;
    private readonly IVaultRepository _vaultRepository;
    private readonly IUnitOfWork _unitOfWork;

    public InitializeVaultHandler(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _vaultRepository = vaultRepository ?? throw new ArgumentNullException(nameof(vaultRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<InitializeVaultResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidCurrentUserIdentityException();

        var existingVault = await _vaultRepository.GetByUserIdAsync(userId, cancellationToken);
        if (existingVault is not null)
        {
            return new InitializeVaultResult(Map(existingVault), Created: false);
        }

        var vault = new VaultEntity(userId);
        await _vaultRepository.AddAsync(vault, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new InitializeVaultResult(Map(vault), Created: true);
        }
        catch (DuplicateVaultException)
        {
            var concurrentVault = await _vaultRepository.GetByUserIdAsync(userId, cancellationToken);
            if (concurrentVault is null)
            {
                throw;
            }

            return new InitializeVaultResult(Map(concurrentVault), Created: false);
        }
    }

    private static VaultResponse Map(VaultEntity vault) =>
        new(
            vault.Id,
            new DateTimeOffset(DateTime.SpecifyKind(vault.CreatedAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(vault.UpdatedAt, DateTimeKind.Utc)));
}
