using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PasswordManager.Application.Exceptions;
using PasswordManager.Application.Interfaces.Authentication;

namespace PasswordManager.API.Services;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub);

            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                throw new InvalidCurrentUserIdentityException();
            }

            return userId;
        }
    }
}
