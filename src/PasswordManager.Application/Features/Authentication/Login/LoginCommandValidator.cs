using System.Net.Mail;
using System.Text.RegularExpressions;
using PasswordManager.Application.Exceptions;

namespace PasswordManager.Application.Features.Authentication.Login;

public static partial class LoginCommandValidator
{
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Validate(LoginCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

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
