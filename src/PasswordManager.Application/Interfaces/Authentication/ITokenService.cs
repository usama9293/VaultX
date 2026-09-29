using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Interfaces.Authentication;

public interface ITokenService
{
    string CreateToken(User user);
    bool ValidateToken(string token);
}
