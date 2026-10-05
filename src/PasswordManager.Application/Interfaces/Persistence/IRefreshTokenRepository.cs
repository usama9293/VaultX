using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Interfaces.Persistence;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task LockUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<RefreshToken>> GetByFamilyIdAsync(Guid familyId, CancellationToken cancellationToken = default);
    Task<List<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
    Task UpdateAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
}
