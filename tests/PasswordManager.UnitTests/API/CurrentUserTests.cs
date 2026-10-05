using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PasswordManager.API.Services;
using PasswordManager.Application.Exceptions;

namespace PasswordManager.UnitTests.API;

public class CurrentUserTests
{
    [Fact]
    public void UserId_AuthenticatedPrincipalWithValidNameIdentifier_ReturnsUserId()
    {
        var expectedUserId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, expectedUserId.ToString())],
            authenticationType: "Bearer")));

        Assert.Equal(expectedUserId, currentUser.UserId);
    }

    [Fact]
    public void UserId_AnonymousPrincipal_ReturnsNull()
    {
        var currentUser = CreateCurrentUser(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_AuthenticatedPrincipalWithoutUserIdClaim_ThrowsInvalidIdentityException()
    {
        var currentUser = CreateCurrentUser(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            authenticationType: "Bearer")));

        Assert.Throws<InvalidCurrentUserIdentityException>(() => currentUser.UserId);
    }

    [Fact]
    public void UserId_AuthenticatedPrincipalWithMalformedUserIdClaim_ThrowsInvalidIdentityException()
    {
        var currentUser = CreateCurrentUser(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
            authenticationType: "Bearer")));

        Assert.Throws<InvalidCurrentUserIdentityException>(() => currentUser.UserId);
    }

    private static CurrentUser CreateCurrentUser(ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext
        {
            User = principal
        };

        return new CurrentUser(new HttpContextAccessor
        {
            HttpContext = httpContext
        });
    }
}
