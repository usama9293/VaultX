using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Features.Users.GetById;

namespace PasswordManager.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IGetUserByIdHandler _getUserByIdHandler;

    public UsersController(IGetUserByIdHandler getUserByIdHandler)
    {
        _getUserByIdHandler = getUserByIdHandler ?? throw new ArgumentNullException(nameof(getUserByIdHandler));
    }

    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _getUserByIdHandler.HandleAsync(
            new GetUserByIdQuery(userId),
            cancellationToken);

        return user is null ? NotFound() : Ok(user);
    }
}
