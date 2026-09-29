using System.Text;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Application.Interfaces.Security;
using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Features.Authentication.Register;

public class RegisterUserHandler : IRegisterUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterUserHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<UserResponse> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        // Validate command inputs and password policy
        RegisterUserCommandValidator.Validate(command);

        // Normalize email
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        // Application-level check for duplicate email
        var existingUser = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            throw new DuplicateEmailException("A user with this email already exists.");
        }

        // Hash password securely through abstraction
        var hashedPassword = await _passwordHasher.HashPasswordAsync(command.Password, cancellationToken);
        var passwordHashBytes = Encoding.UTF8.GetBytes(hashedPassword);

        // Create domain entity
        var user = new User(normalizedEmail, passwordHashBytes);

        // Persist through repository and unit of work
        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Return safe response
        return new UserResponse(
            user.Id,
            user.Email,
            user.CreatedAt,
            user.UpdatedAt);
    }
}
