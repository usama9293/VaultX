namespace PasswordManager.Application.Features.Authentication.Login;

public sealed record LoginCommand(
    string Email,
    string Password);
