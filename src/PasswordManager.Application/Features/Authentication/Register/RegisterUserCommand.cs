namespace PasswordManager.Application.Features.Authentication.Register;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string ConfirmPassword);
