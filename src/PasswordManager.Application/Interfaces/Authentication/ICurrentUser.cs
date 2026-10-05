namespace PasswordManager.Application.Interfaces.Authentication;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
