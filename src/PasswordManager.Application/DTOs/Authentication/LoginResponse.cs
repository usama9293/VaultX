namespace PasswordManager.Application.DTOs.Authentication;

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAt);
