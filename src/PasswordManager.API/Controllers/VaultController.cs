using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Features.Vault.GetCurrent;
using PasswordManager.Application.Features.Vault.Initialize;

namespace PasswordManager.API.Controllers;

[ApiController]
[Route("api/vault")]
[Authorize]
public sealed class VaultController : ControllerBase
{
    private readonly IInitializeVaultHandler _initializeVaultHandler;
    private readonly IGetCurrentVaultHandler _getCurrentVaultHandler;

    public VaultController(
        IInitializeVaultHandler initializeVaultHandler,
        IGetCurrentVaultHandler getCurrentVaultHandler)
    {
        _initializeVaultHandler = initializeVaultHandler
            ?? throw new ArgumentNullException(nameof(initializeVaultHandler));
        _getCurrentVaultHandler = getCurrentVaultHandler
            ?? throw new ArgumentNullException(nameof(getCurrentVaultHandler));
    }

    /// <summary>Gets the authenticated user's vault, if it has been initialized.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(VaultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var vault = await _getCurrentVaultHandler.HandleAsync(cancellationToken);
        return vault is null ? NotFound() : Ok(vault);
    }

    /// <summary>Initializes or returns the authenticated user's vault.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(VaultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VaultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Initialize(CancellationToken cancellationToken)
    {
        var result = await _initializeVaultHandler.HandleAsync(cancellationToken);
        return result.Created
            ? CreatedAtAction(nameof(GetCurrent), result.Vault)
            : Ok(result.Vault);
    }
}
