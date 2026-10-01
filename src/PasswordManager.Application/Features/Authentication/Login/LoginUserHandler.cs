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
            // Generic authentication failure to prevent user enumeration
            throw new InvalidCredentialsException("Invalid email or password.");
        }

        // Verify password against stored hash using password-hashing abstraction
        var storedHash = Encoding.UTF8.GetString(user.PasswordHash);
        var isPasswordValid = await _passwordHasher.VerifyPasswordAsync(command.Password, storedHash, cancellationToken);
        if (!isPasswordValid)
        {
            // Generic authentication failure to prevent user enumeration
            throw new InvalidCredentialsException("Invalid email or password.");
        }

        // Generate short-lived JWT access token
        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user);

        // Generate cryptographically secure opaque refresh token
        var (rawRefreshToken, tokenHash, refreshExpiresAt) = _tokenService.GenerateRefreshToken();

        // Create and persist refresh token session (preserves existing sessions)
        var refreshToken = new RefreshToken(user.Id, tokenHash, refreshExpiresAt);
        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            accessToken,
            accessExpiresAt,
            rawRefreshToken,
            refreshExpiresAt);
    }
}
