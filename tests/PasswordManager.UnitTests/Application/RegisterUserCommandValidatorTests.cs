using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Features.Authentication.Register;
using Xunit;

namespace PasswordManager.UnitTests.Application;

public class RegisterUserCommandValidatorTests
{
    private const string ValidEmail = "user@example.com";
    private const string ValidPassword = "CorrectHorse!Battery99#";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyEmail_ThrowsValidationException(string? email)
    {
        var command = new RegisterUserCommand(email!, ValidPassword, ValidPassword);

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Email)));
        Assert.Contains("Email is required.", ex.Errors[nameof(command.Email)]);
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@missingusername.com")]
    [InlineData("missingdomain@.com")]
    [InlineData("missingdot@com")]
    [InlineData("trailingdot@example.com.")]
    [InlineData("user@example..com")]
    public void Validate_InvalidEmailFormat_ThrowsValidationException(string invalidEmail)
    {
        var command = new RegisterUserCommand(invalidEmail, ValidPassword, ValidPassword);

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Email)));
        Assert.Contains("Email format is invalid.", ex.Errors[nameof(command.Email)]);
    }

    [Fact]
    public void Validate_EmailExceeding320Characters_ThrowsValidationException()
    {
        var longEmail = new string('a', 315) + "@test.com";
        var command = new RegisterUserCommand(longEmail, ValidPassword, ValidPassword);

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Email)));
        Assert.Contains("Email must not exceed 320 characters.", ex.Errors[nameof(command.Email)]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyPassword_ThrowsValidationException(string? password)
    {
        var command = new RegisterUserCommand(ValidEmail, password!, password!);

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Password)));
        Assert.Contains("Password is required.", ex.Errors[nameof(command.Password)]);
    }

    [Fact]
    public void Validate_PasswordTooShort_ThrowsValidationException()
    {
        var command = new RegisterUserCommand(ValidEmail, "Short1!a", "Short1!a");

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Password)));
        Assert.Contains("Password must be at least 12 characters long.", ex.Errors[nameof(command.Password)]);
    }

    [Fact]
    public void Validate_PasswordMissingUppercase_ThrowsValidationException()
    {
        var command = new RegisterUserCommand(ValidEmail, "lowercaseonly123!", "lowercaseonly123!");

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Password)));
        Assert.Contains("Password must contain at least one uppercase letter.", ex.Errors[nameof(command.Password)]);
    }

    [Fact]
    public void Validate_PasswordMissingLowercase_ThrowsValidationException()
    {
        var command = new RegisterUserCommand(ValidEmail, "UPPERCASEONLY123!", "UPPERCASEONLY123!");

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Password)));
        Assert.Contains("Password must contain at least one lowercase letter.", ex.Errors[nameof(command.Password)]);
    }

    [Fact]
    public void Validate_PasswordMissingDigit_ThrowsValidationException()
    {
        var command = new RegisterUserCommand(ValidEmail, "NoDigitsInPassword!", "NoDigitsInPassword!");

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Password)));
        Assert.Contains("Password must contain at least one digit.", ex.Errors[nameof(command.Password)]);
    }

    [Fact]
    public void Validate_PasswordMissingSpecialCharacter_ThrowsValidationException()
    {
        var command = new RegisterUserCommand(ValidEmail, "NoSpecialCharacters123", "NoSpecialCharacters123");

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.Password)));
        Assert.Contains("Password must contain at least one special character.", ex.Errors[nameof(command.Password)]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyConfirmPassword_ThrowsValidationException(string? confirmPassword)
    {
        var command = new RegisterUserCommand(ValidEmail, ValidPassword, confirmPassword!);

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.ConfirmPassword)));
        Assert.Contains("Password confirmation is required.", ex.Errors[nameof(command.ConfirmPassword)]);
    }

    [Fact]
    public void Validate_PasswordConfirmationMismatch_ThrowsValidationException()
    {
        var command = new RegisterUserCommand(ValidEmail, ValidPassword, "DifferentPassword123!");

        var ex = Assert.Throws<ValidationException>(() => RegisterUserCommandValidator.Validate(command));
        Assert.True(ex.Errors.ContainsKey(nameof(command.ConfirmPassword)));
        Assert.Contains("Passwords do not match.", ex.Errors[nameof(command.ConfirmPassword)]);
    }

    [Fact]
    public void Validate_ValidCommand_PassesWithoutExceptions()
    {
        var command = new RegisterUserCommand(ValidEmail, ValidPassword, ValidPassword);

        var exception = Record.Exception(() => RegisterUserCommandValidator.Validate(command));
        Assert.Null(exception);
    }
}
