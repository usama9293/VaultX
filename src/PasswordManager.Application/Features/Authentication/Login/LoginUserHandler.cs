using System.Text;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Application.Interfaces.Persistence;
using PasswordManager.Application.Interfaces.Security;
using PasswordManager.Domain.Entities;

namespace PasswordManager.Application.Features.Authentication.Login;

public class LoginUserHandler : ILoginUserHandler
{
    private const string UnknownAccountDummyPasswordHash =
        "$pbkdf2-sha256$i=100000$s=VmF1bHRYLUR1bW15LVNhbHQh$h=h4e8YZTbt4pwoh/Jy+o1ZqiUzDK50A682L/2eKkVYLY=";

    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;

    public LoginUserHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        // Validate command inputs
        LoginCommandValidator.Validate(command);

        // Normalize email consistently with Registration
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        // Retrieve user by normalized email
        var user = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null)
        {
            await _passwordHasher.VerifyPasswordAsync(
                command.Password,
                UnknownAccountDummyPasswordHash,
                cancellationToken);
            // Generic authentication failure to prevent user enumeration
            throw InvalidCredentials();
        }

        // Verify password against stored hash using password-hashing abstraction
        var storedHash = Encoding.UTF8.GetString(user.PasswordHash);
        var isPasswordValid = await _passwordHasher.VerifyPasswordAsync(command.Password, storedHash, cancellationToken);

        var loginResult = await _unitOfWork.ExecuteInTransactionAsync<LoginResult?>(
            async transactionToken =>
            {
                await _userRepository.LockUserAsync(user.Id, transactionToken);

                var currentUser = await _userRepository.GetFreshByIdAsync(user.Id, transactionToken);
                if (currentUser is null)
                {
                    return null;
                }

                var now = DateTime.UtcNow;
                currentUser.ClearExpiredLockout(now);
                if (currentUser.IsLocked(now))
                {
                    return null;
                }

                if (!isPasswordValid)
                {
                    currentUser.RecordFailedLogin(now);
                    await _userRepository.UpdateAsync(currentUser, transactionToken);
                    await _unitOfWork.SaveChangesAsync(transactionToken);
                    return null;
                }

                currentUser.ResetLoginFailures();

                var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(currentUser);
                var (rawRefreshToken, tokenHash, refreshExpiresAt) =
                    _tokenService.GenerateRefreshToken();
                var refreshToken = new RefreshToken(currentUser.Id, tokenHash, refreshExpiresAt);

                await _userRepository.UpdateAsync(currentUser, transactionToken);
                await _refreshTokenRepository.AddAsync(refreshToken, transactionToken);
                await _unitOfWork.SaveChangesAsync(transactionToken);

                return new LoginResult(
                    accessToken,
                    accessExpiresAt,
                    rawRefreshToken,
                    refreshExpiresAt);
            },
            cancellationToken);

        return loginResult ?? throw InvalidCredentials();
    }

    private static InvalidCredentialsException InvalidCredentials()
    {
        return new InvalidCredentialsException("Invalid email or password.");
    }
}
