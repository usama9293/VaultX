using Microsoft.AspNetCore.Mvc;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Features.Authentication.Register;

namespace PasswordManager.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IRegisterUserHandler _registerUserHandler;

    public AuthController(IRegisterUserHandler registerUserHandler)
    {
        _registerUserHandler = registerUserHandler ?? throw new ArgumentNullException(nameof(registerUserHandler));
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
}
