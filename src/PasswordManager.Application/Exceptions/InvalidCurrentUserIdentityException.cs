namespace PasswordManager.Application.Exceptions;

public sealed class InvalidCurrentUserIdentityException : Exception
{
    public InvalidCurrentUserIdentityException()
        : base("The authenticated identity does not contain a valid user ID.")
    {
    }
}
