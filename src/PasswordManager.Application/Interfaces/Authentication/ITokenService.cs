using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Interfaces.Authentication;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(User user);
    (string RawToken, string TokenHash, DateTime ExpiresAt) GenerateRefreshToken();
    string HashRefreshToken(string rawToken);
}
