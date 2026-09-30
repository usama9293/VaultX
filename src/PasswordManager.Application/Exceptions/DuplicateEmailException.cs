namespace PasswordManager.Application.Exceptions;

public class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string message = "A user with the specified email already exists.")
        : base(message)
    {
    }

    public DuplicateEmailException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
