using Microsoft.EntityFrameworkCore;
using PasswordManager.Application.DTOs.Vault;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.Infrastructure.Repositories;

public sealed class VaultEntryRepository : IVaultEntryRepository
{
    private const string LikeEscapeCharacter = "\\";
    private readonly ApplicationDbContext _dbContext;

    public VaultEntryRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<VaultEntryData?> GetByIdAndVaultIdAsync(
        Guid id,
        Guid vaultId,
        CancellationToken cancellationToken = default) =>
        _dbContext.VaultEntries
            .AsNoTracking()
            .Where(entry => entry.Id == id && entry.VaultId == vaultId)
            .Select(entry => new VaultEntryData(
                entry.Id,
                entry.Title,
                entry.WebsiteUrl,
                entry.Username,
                entry.Notes,
                entry.CreatedAt,
                entry.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<VaultEntryListItemData>> GetPageByVaultIdAsync(
        Guid vaultId,
        string? query,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var entries = _dbContext.VaultEntries
            .AsNoTracking()
            .Where(entry => entry.VaultId == vaultId);

        if (!string.IsNullOrEmpty(query))
        {
            var escapedQuery = EscapeLikeLiteral(query.ToLowerInvariant());
            var pattern = $"%{escapedQuery}%";
            entries = entries.Where(entry =>
                EF.Functions.Like(entry.Title.ToLower(), pattern, LikeEscapeCharacter)
                || (entry.WebsiteUrl != null
                    && EF.Functions.Like(entry.WebsiteUrl.ToLower(), pattern, LikeEscapeCharacter))
                || EF.Functions.Like(entry.Username.ToLower(), pattern, LikeEscapeCharacter));
        }

        return await entries
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .Select(entry => new VaultEntryListItemData(
                entry.Id,
                entry.Title,
                entry.WebsiteUrl,
                entry.Username,
                entry.CreatedAt,
                entry.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(VaultEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _dbContext.VaultEntries.AddAsync(entry, cancellationToken);
    }

    public async Task<bool> UpdateByIdAndVaultIdAsync(
        Guid id,
        Guid vaultId,
        string title,
        string? websiteUrl,
        string username,
        string? notes,
        DateTime updatedAt,
        CancellationToken cancellationToken = default)
    {
        var updatedRows = await _dbContext.VaultEntries
            .Where(entry => entry.Id == id && entry.VaultId == vaultId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entry => entry.Title, title)
                    .SetProperty(entry => entry.WebsiteUrl, websiteUrl)
                    .SetProperty(entry => entry.Username, username)
                    .SetProperty(entry => entry.Notes, notes)
                    .SetProperty(entry => entry.UpdatedAt, updatedAt),
                cancellationToken);

        return updatedRows != 0;
    }

    public async Task<bool> DeleteByIdAndVaultIdAsync(
        Guid id,
        Guid vaultId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.VaultEntries
            .Where(entry => entry.Id == id && entry.VaultId == vaultId)
            .ExecuteDeleteAsync(cancellationToken) != 0;

    private static string EscapeLikeLiteral(string value) =>
        value.Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);
}
