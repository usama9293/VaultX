namespace PasswordManager.Application.Exceptions;

public sealed class DuplicateVaultException : Exception
{
    public DuplicateVaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
