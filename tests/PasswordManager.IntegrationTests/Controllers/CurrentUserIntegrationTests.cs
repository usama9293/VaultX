using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PasswordManager.Application.DTOs.Authentication;

namespace PasswordManager.IntegrationTests.Controllers;

public class CurrentUserIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CurrentUserIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuthenticatedRequest_ResolvesUserIdFromValidatedJwt()
    {
        const string email = "current.user.identity@vaultx.local";
        const string password = "VaultX@SecurePass2026!";

        var registrationResponse = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);

        var registeredUser = await registrationResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(registeredUser);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var accessToken = loginJson.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-auth/current-user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var identityResponse = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, identityResponse.StatusCode);
        var identity = await identityResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(identity);
        Assert.Equal(registeredUser.Id, identity.UserId);
    }

    private sealed record CurrentUserResponse(Guid UserId);
}
