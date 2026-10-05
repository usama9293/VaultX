using Microsoft.AspNetCore.Mvc;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Features.Authentication.Login;
using PasswordManager.Application.Features.Authentication.Logout;
using PasswordManager.Application.Features.Authentication.Register;
using PasswordManager.Application.Features.Authentication.Refresh;
using PasswordManager.Application.Exceptions;

namespace PasswordManager.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IRegisterUserHandler _registerUserHandler;
    private readonly ILoginUserHandler _loginUserHandler;
    private readonly ILogoutUserHandler _logoutUserHandler;
    private readonly IRefreshTokenHandler _refreshTokenHandler;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        IRegisterUserHandler registerUserHandler,
        ILoginUserHandler loginUserHandler,
        ILogoutUserHandler logoutUserHandler,
        IRefreshTokenHandler refreshTokenHandler,
        IWebHostEnvironment environment)
    {
        _registerUserHandler = registerUserHandler ?? throw new ArgumentNullException(nameof(registerUserHandler));
        _loginUserHandler = loginUserHandler ?? throw new ArgumentNullException(nameof(loginUserHandler));
        _logoutUserHandler = logoutUserHandler ?? throw new ArgumentNullException(nameof(logoutUserHandler));
        _refreshTokenHandler = refreshTokenHandler ?? throw new ArgumentNullException(nameof(refreshTokenHandler));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth"
        };

        try
        {
            var result = await _refreshTokenHandler.HandleAsync(
                new RefreshCommand(Request.Cookies["refreshToken"]),
                cancellationToken);

            cookieOptions.Expires = result.RefreshTokenExpiresAt;
            Response.Cookies.Append("refreshToken", result.RawRefreshToken, cookieOptions);
            return Ok(new LoginResponse(result.AccessToken, result.AccessTokenExpiresAt));
        }
        catch (InvalidCredentialsException)
        {
            Response.Cookies.Delete("refreshToken", cookieOptions);
            throw;
        }
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterUserCommand(
            request.Email,
            request.Password,
            request.ConfirmPassword);

        var response = await _registerUserHandler.HandleAsync(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(
            request.Email,
            request.Password);

        var result = await _loginUserHandler.HandleAsync(command, cancellationToken);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = result.RefreshTokenExpiresAt,
            Path = "/api/auth"
        };

        Response.Cookies.Append("refreshToken", result.RawRefreshToken, cookieOptions);

        return Ok(new LoginResponse(result.AccessToken, result.AccessTokenExpiresAt));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var rawRefreshToken = Request.Cookies["refreshToken"];

        var command = new LogoutCommand(rawRefreshToken);
        await _logoutUserHandler.HandleAsync(command, cancellationToken);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth"
        };

        Response.Cookies.Delete("refreshToken", cookieOptions);

        return NoContent();
    }
}
