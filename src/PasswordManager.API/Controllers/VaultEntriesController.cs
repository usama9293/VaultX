using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Features.Vault.Entries;

namespace PasswordManager.API.Controllers;

[ApiController]
[Route("api/vault/entries")]
[Authorize]
[RequestSizeLimit(32 * 1024)]
public sealed class VaultEntriesController : ControllerBase
{
    private readonly IVaultEntryService _entryService;

    public VaultEntriesController(IVaultEntryService entryService)
    {
        _entryService = entryService ?? throw new ArgumentNullException(nameof(entryService));
    }

    /// <summary>Creates metadata for a new entry in the authenticated user's vault.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(VaultEntryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Create(
        [FromBody] CreateVaultEntryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await _entryService.CreateAsync(request, cancellationToken);
        return entry is null
            ? GenericNotFound()
            : CreatedAtAction(nameof(GetById), new { entryId = entry.Id }, entry);
    }

    /// <summary>Lists and searches the authenticated user's entry metadata.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(VaultEntryPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPage(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _entryService.GetPageAsync(q, page, pageSize, cancellationToken);
        return result is null ? GenericNotFound() : Ok(result);
    }

    /// <summary>Gets metadata for one entry belonging to the authenticated user's vault.</summary>
    [HttpGet("{entryId:regex(.+)}")]
    [ProducesResponseType(typeof(VaultEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string entryId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(entryId, out var parsedEntryId) || parsedEntryId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid entry id.",
                Detail = "The entry id is invalid."
            });
        }

        var entry = await _entryService.GetByIdAsync(parsedEntryId, cancellationToken);
        return entry is null ? GenericNotFound() : Ok(entry);
    }

    /// <summary>Replaces editable metadata for one owned entry.</summary>
    [HttpPut("{entryId:regex(.+)}")]
    [ProducesResponseType(typeof(VaultEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Update(
        string entryId,
        [FromBody] UpdateVaultEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(entryId, out var parsedEntryId) || parsedEntryId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid entry id.",
                Detail = "The entry id is invalid."
            });
        }

        var entry = await _entryService.UpdateAsync(parsedEntryId, request, cancellationToken);
        return entry is null ? GenericNotFound() : Ok(entry);
    }

    /// <summary>Deletes one entry belonging to the authenticated user's vault.</summary>
    [HttpDelete("{entryId:regex(.+)}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string entryId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(entryId, out var parsedEntryId) || parsedEntryId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid entry id.",
                Detail = "The entry id is invalid."
            });
        }

        var deleted = await _entryService.DeleteAsync(parsedEntryId, cancellationToken);
        return deleted ? NoContent() : GenericNotFound();
    }

    private IActionResult GenericNotFound() =>
        new ContentResult
        {
            StatusCode = StatusCodes.Status404NotFound,
            ContentType = "application/problem+json",
            Content = """{"type":"about:blank","title":"Not Found","status":404,"detail":"The requested resource was not found."}"""
        };
}
