namespace PasswordManager.Application.DTOs.Vault;

/// <summary>A page of the authenticated user's entry metadata.</summary>
public sealed record VaultEntryPageResponse(
    IReadOnlyList<VaultEntryListItemResponse> Items,
    int Page,
    int PageSize,
    bool HasMore);
