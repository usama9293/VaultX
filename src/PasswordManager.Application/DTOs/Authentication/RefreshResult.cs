namespace PasswordManager.Application.DTOs.Authentication;

public sealed record RefreshResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RawRefreshToken,
    DateTime RefreshTokenExpiresAt);
