using System.Net.Mail;
using System.Text.RegularExpressions;
using PasswordManager.Application.Exceptions;

namespace PasswordManager.Application.Features.Authentication.Register;

public static partial class RegisterUserCommandValidator
{
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Validate(RegisterUserCommand command)
    {
        var errors = new Dictionary<string, List<string>>();

        void AddError(string propertyName, string error)
        {
            if (!errors.TryGetValue(propertyName, out var errorList))
            {
                errorList = new List<string>();
                errors[propertyName] = errorList;
            }

            errorList.Add(error);
        }

        // Email validation
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            AddError(nameof(command.Email), "Email is required.");
        }
        else
        {
            var trimmedEmail = command.Email.Trim();
            if (trimmedEmail.Length > 320)
            {
                AddError(nameof(command.Email), "Email must not exceed 320 characters.");
            }
            else if (!IsValidEmail(trimmedEmail))
            {
                AddError(nameof(command.Email), "Email format is invalid.");
            }
        }

        // Password validation
        if (string.IsNullOrWhiteSpace(command.Password))
        {
            AddError(nameof(command.Password), "Password is required.");
        }
        else
        {
            if (command.Password.Length < 12)
            {
                AddError(nameof(command.Password), "Password must be at least 12 characters long.");
            }

            if (command.Password.Length > 128)
            {
                AddError(nameof(command.Password), "Password must not exceed 128 characters.");
            }

            if (!command.Password.Any(char.IsUpper))
            {
                AddError(nameof(command.Password), "Password must contain at least one uppercase letter.");
            }

            if (!command.Password.Any(char.IsLower))
            {
                AddError(nameof(command.Password), "Password must contain at least one lowercase letter.");
            }

            if (!command.Password.Any(char.IsDigit))
            {
                AddError(nameof(command.Password), "Password must contain at least one digit.");
            }

            if (!command.Password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                AddError(nameof(command.Password), "Password must contain at least one special character.");
            }
        }

        // ConfirmPassword validation
        if (string.IsNullOrWhiteSpace(command.ConfirmPassword))
        {
            AddError(nameof(command.ConfirmPassword), "Password confirmation is required.");
        }
        else if (!string.IsNullOrEmpty(command.Password) && command.Password != command.ConfirmPassword)
        {
            AddError(nameof(command.ConfirmPassword), "Passwords do not match.");
        }

        if (errors.Count > 0)
        {
            var formattedErrors = errors.ToDictionary(k => k.Key, v => v.Value.ToArray());
            throw new ValidationException(formattedErrors);
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.EndsWith(".") || email.StartsWith(".") || email.Contains(".."))
        {
            return false;
        }

        if (!EmailRegex.IsMatch(email))
        {
            return false;
        }

        try
        {
            var mailAddress = new MailAddress(email);
            return mailAddress.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
