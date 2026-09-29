namespace PasswordManager.Application.DTOs.Authentication;

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string ConfirmPassword);
