using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PasswordManager.Application.Interfaces.Authentication;

namespace PasswordManager.IntegrationTests.Controllers;

[ApiController]
[Route("api/test-auth")]
public class TestAuthController : ControllerBase
{
    private readonly ICurrentUser _currentUser;

    public TestAuthController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    [Authorize]
    [HttpGet("protected")]
    public IActionResult ProtectedEndpoint()
    {
        return Ok(new { message = "Authorized" });
    }

    [Authorize]
    [HttpGet("current-user")]
    public IActionResult CurrentUserIdentity()
    {
        return Ok(new { userId = _currentUser.UserId });
    }
}
