namespace PasswordManager.Application.DTOs.Authentication;

public sealed record LoginRequest(
    string Email,
    string Password);
