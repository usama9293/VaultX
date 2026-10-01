namespace PasswordManager.Application.DTOs.Authentication;

public sealed record LoginResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RawRefreshToken,
    DateTime RefreshTokenExpiresAt);
