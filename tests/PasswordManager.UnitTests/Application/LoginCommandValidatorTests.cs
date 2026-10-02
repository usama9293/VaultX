using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Authentication.Login;
using Xunit;

namespace PasswordManager.UnitTests.Application;

public class LoginCommandValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_EmptyEmail_ThrowsValidationException(string? email)
    {
        var command = new LoginCommand(email!, "ValidPassword123!");

        var ex = Assert.Throws<ValidationException>(() => LoginCommandValidator.Validate(command));

        Assert.True(ex.Errors.ContainsKey(nameof(LoginCommand.Email)));
        Assert.Contains("Email is required.", ex.Errors[nameof(LoginCommand.Email)]);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("@domain.com")]
    [InlineData("user..name@domain.com")]
    [InlineData("user@domain.com.")]
    public void Validate_InvalidEmailFormat_ThrowsValidationException(string invalidEmail)
    {
        var command = new LoginCommand(invalidEmail, "ValidPassword123!");

        var ex = Assert.Throws<ValidationException>(() => LoginCommandValidator.Validate(command));

        Assert.True(ex.Errors.ContainsKey(nameof(LoginCommand.Email)));
        Assert.Contains("Email format is invalid.", ex.Errors[nameof(LoginCommand.Email)]);
    }

    [Fact]
    public void Validate_OversizedEmail_ThrowsValidationException()
    {
        var oversizedEmail = new string('a', 315) + "@test.com"; // > 320 chars
        var command = new LoginCommand(oversizedEmail, "ValidPassword123!");

        var ex = Assert.Throws<ValidationException>(() => LoginCommandValidator.Validate(command));

        Assert.True(ex.Errors.ContainsKey(nameof(LoginCommand.Email)));
        Assert.Contains("Email must not exceed 320 characters.", ex.Errors[nameof(LoginCommand.Email)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_EmptyPassword_ThrowsValidationException(string? password)
    {
        var command = new LoginCommand("valid@example.com", password!);

        var ex = Assert.Throws<ValidationException>(() => LoginCommandValidator.Validate(command));

        Assert.True(ex.Errors.ContainsKey(nameof(LoginCommand.Password)));
        Assert.Contains("Password is required.", ex.Errors[nameof(LoginCommand.Password)]);
    }

    [Fact]
    public void Validate_ValidCommand_DoesNotThrow()
    {
        var command = new LoginCommand("valid.user@example.com", "AnyNonEmptyPassword123!");

        var exception = Record.Exception(() => LoginCommandValidator.Validate(command));

        Assert.Null(exception);
    }
}
