using System.Text;
using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using VaultEntity = PasswordManager.Domain.Entities.Vault;

namespace PasswordManager.Application.Features.Vault.Entries;

public sealed class VaultEntryService : IVaultEntryService
{
    private const int TitleMaximumLength = 255;
    private const int WebsiteUrlMaximumLength = 2048;
    private const int UsernameMaximumLength = 255;
    private const int NotesMaximumLength = 2000;
    private const int SearchMaximumLength = 128;
    private const int MaximumPageSize = 100;

    private readonly ICurrentUser _currentUser;
    private readonly IVaultRepository _vaultRepository;
    private readonly IVaultEntryRepository _entryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public VaultEntryService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IVaultEntryRepository entryRepository,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _vaultRepository = vaultRepository ?? throw new ArgumentNullException(nameof(vaultRepository));
        _entryRepository = entryRepository ?? throw new ArgumentNullException(nameof(entryRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<VaultEntryResponse?> CreateAsync(
        CreateVaultEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var vault = await GetCurrentVaultAsync(cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var metadata = ValidateMetadata(request.Title, request.WebsiteUrl, request.Username, request.Notes);
        var entry = new VaultEntry(
            vault.Id,
            metadata.Title,
            metadata.Username,
            metadata.WebsiteUrl,
            metadata.Notes);

        await _entryRepository.AddAsync(entry, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entry);
    }

    public async Task<VaultEntryPageResponse?> GetPageAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeSearchQuery(query);
        if (page < 1 || pageSize < 1 || pageSize > MaximumPageSize
            || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw ValidationError(
                nameof(page),
                "Page must be positive and page size must be between 1 and 100.");
        }

        var vault = await GetCurrentVaultAsync(cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var skip = (int)((long)(page - 1) * pageSize);
        var entries = await _entryRepository.GetPageByVaultIdAsync(
            vault.Id,
            normalizedQuery,
            skip,
            pageSize + 1,
            cancellationToken);
        var hasMore = entries.Count > pageSize;
        var items = entries
            .Take(pageSize)
            .Select(entry => new VaultEntryListItemResponse(
                entry.Id,
                entry.Title,
                entry.WebsiteUrl,
                entry.Username,
                ToUtcOffset(entry.CreatedAt),
                ToUtcOffset(entry.UpdatedAt)))
            .ToArray();

        return new VaultEntryPageResponse(items, page, pageSize, hasMore);
    }

    public async Task<VaultEntryResponse?> GetByIdAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
        {
            throw ValidationError(nameof(entryId), "Entry id is invalid.");
        }

        var vault = await GetCurrentVaultAsync(cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var entry = await _entryRepository.GetByIdAndVaultIdAsync(entryId, vault.Id, cancellationToken);
        return entry is null ? null : Map(entry);
    }

    public async Task<VaultEntryResponse?> UpdateAsync(
        Guid entryId,
        UpdateVaultEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (entryId == Guid.Empty)
        {
            throw ValidationError(nameof(entryId), "Entry id is invalid.");
        }

        var vault = await GetCurrentVaultAsync(cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var metadata = ValidateMetadata(request.Title, request.WebsiteUrl, request.Username, request.Notes);
        var updated = await _entryRepository.UpdateByIdAndVaultIdAsync(
            entryId,
            vault.Id,
            metadata.Title,
            metadata.WebsiteUrl,
            metadata.Username,
            metadata.Notes,
            DateTime.UtcNow,
            cancellationToken);
        if (!updated)
        {
            return null;
        }

        var entry = await _entryRepository.GetByIdAndVaultIdAsync(entryId, vault.Id, cancellationToken);
        return entry is null ? null : Map(entry);
    }

    public async Task<bool> DeleteAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
        {
            throw ValidationError(nameof(entryId), "Entry id is invalid.");
        }

        var vault = await GetCurrentVaultAsync(cancellationToken);
        return vault is not null
            && await _entryRepository.DeleteByIdAndVaultIdAsync(entryId, vault.Id, cancellationToken);
    }

    private async Task<VaultEntity?> GetCurrentVaultAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            throw new InvalidCurrentUserIdentityException();
        }

        return await _vaultRepository.GetByUserIdAsync(userId.Value, cancellationToken);
    }

    private static EntryMetadata ValidateMetadata(
        string? title,
        string? websiteUrl,
        string? username,
        string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedTitle = NormalizeRequired(title, TitleMaximumLength, nameof(CreateVaultEntryRequest.Title), errors);
        var normalizedUsername = NormalizeRequired(username, UsernameMaximumLength, nameof(CreateVaultEntryRequest.Username), errors);
        var normalizedWebsiteUrl = NormalizeWebsiteUrl(websiteUrl, errors);
        var normalizedNotes = NormalizeNotes(notes, errors);

        if (errors.Count != 0)
        {
            throw new ValidationException(errors);
        }

        return new EntryMetadata(normalizedTitle!, normalizedWebsiteUrl, normalizedUsername!, normalizedNotes);
    }

    private static string? NormalizeRequired(
        string? value,
        int maximumLength,
        string field,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = ["This field is required."];
            return null;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0)
        {
            errors[field] = ["This field is required."];
            return null;
        }

        if (ScalarCount(normalized) > maximumLength)
        {
            errors[field] = [$"This field must be {maximumLength} characters or fewer."];
            return null;
        }

        if (ContainsControl(normalized, allowNotesWhitespace: false))
        {
            errors[field] = ["Control characters are not allowed."];
            return null;
        }

        return normalized;
    }

    private static string? NormalizeWebsiteUrl(string? value, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (ScalarCount(normalized) > WebsiteUrlMaximumLength)
        {
            errors[nameof(CreateVaultEntryRequest.WebsiteUrl)] = ["Website URL is too long."];
            return null;
        }

        if (ContainsControl(normalized, allowNotesWhitespace: false)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors[nameof(CreateVaultEntryRequest.WebsiteUrl)] =
                ["Website URL must be an absolute HTTP or HTTPS address without user information."];
            return null;
        }

        return normalized;
    }

    private static string? NormalizeNotes(string? value, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormC);
        if (ScalarCount(normalized) > NotesMaximumLength)
        {
            errors[nameof(CreateVaultEntryRequest.Notes)] = ["Notes are too long."];
            return null;
        }

        if (ContainsControl(normalized, allowNotesWhitespace: true))
        {
            errors[nameof(CreateVaultEntryRequest.Notes)] = ["Unsupported control characters are not allowed."];
            return null;
        }

        return normalized;
    }

    private static string? NormalizeSearchQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (ScalarCount(normalized) > SearchMaximumLength)
        {
            throw ValidationError("q", "Search query must be 128 characters or fewer.");
        }

        if (ContainsControl(normalized, allowNotesWhitespace: false))
        {
            throw ValidationError("q", "Search query contains unsupported characters.");
        }

        return normalized;
    }

    private static bool ContainsControl(string value, bool allowNotesWhitespace) =>
        value.EnumerateRunes().Any(rune =>
            Rune.IsControl(rune)
            && !(allowNotesWhitespace && rune.Value is '\r' or '\n' or '\t'));

    private static int ScalarCount(string value) => value.EnumerateRunes().Count();

    private static ValidationException ValidationError(string field, string message) =>
        new(field, message);

    private static VaultEntryResponse Map(VaultEntry entry) =>
        new(
            entry.Id,
            entry.Title,
            entry.WebsiteUrl,
            entry.Username,
            entry.Notes,
            ToUtcOffset(entry.CreatedAt),
            ToUtcOffset(entry.UpdatedAt));

    private static VaultEntryResponse Map(VaultEntryData entry) =>
        new(
            entry.Id,
            entry.Title,
            entry.WebsiteUrl,
            entry.Username,
            entry.Notes,
            ToUtcOffset(entry.CreatedAt),
            ToUtcOffset(entry.UpdatedAt));

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record EntryMetadata(string Title, string? WebsiteUrl, string Username, string? Notes);
}
