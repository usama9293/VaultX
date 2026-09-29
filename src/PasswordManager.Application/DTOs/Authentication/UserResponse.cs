namespace PasswordManager.Application.DTOs.Authentication;

public sealed record UserResponse(
    Guid Id,
    string Email,
    DateTime CreatedAt,
    DateTime UpdatedAt);
