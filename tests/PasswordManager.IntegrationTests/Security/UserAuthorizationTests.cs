using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PasswordManager.Application.DTOs.Authentication;

namespace PasswordManager.IntegrationTests.Security;

public class UserAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UserAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetUser_OwnerAccess_ReturnsSafeProfile()
    {
        var (userId, email, accessToken) = await CreateAuthenticatedUserAsync();

        var response = await SendGetUserRequestAsync(userId, accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var profile = json.RootElement;
        Assert.Equal(userId, profile.GetProperty("id").GetGuid());
        Assert.Equal(email, profile.GetProperty("email").GetString());
        Assert.True(profile.TryGetProperty("createdAt", out _));
        Assert.True(profile.TryGetProperty("updatedAt", out _));
        Assert.False(profile.TryGetProperty("password", out _));
        Assert.False(profile.TryGetProperty("passwordHash", out _));
        Assert.False(profile.TryGetProperty("refreshTokens", out _));
        Assert.False(profile.TryGetProperty("vault", out _));
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshTokens", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUser_AnonymousRequest_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_DifferentAuthenticatedUser_ReturnsNotFound()
    {
        var (userAId, _, _) = await CreateAuthenticatedUserAsync();
        var (_, _, userBAccessToken) = await CreateAuthenticatedUserAsync();

        var response = await SendGetUserRequestAsync(userAId, userBAccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_NonexistentUser_ReturnsNotFound()
    {
        var (_, _, accessToken) = await CreateAuthenticatedUserAsync();

        var response = await SendGetUserRequestAsync(Guid.NewGuid(), accessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_ClientSuppliedIdentityDoesNotOverrideAuthenticatedUser()
    {
        var (userAId, _, _) = await CreateAuthenticatedUserAsync();
        var (_, _, userBAccessToken) = await CreateAuthenticatedUserAsync();

        using var request = CreateGetUserRequest(userAId, userBAccessToken);
        request.Headers.Add("X-UserId", userAId.ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_MalformedGuidRoute_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/users/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid UserId, string Email, string AccessToken)> CreateAuthenticatedUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@vaultx.local";
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

        return (registeredUser.Id, email, accessToken!);
    }

    private Task<HttpResponseMessage> SendGetUserRequestAsync(Guid userId, string accessToken)
    {
        return _client.SendAsync(CreateGetUserRequest(userId, accessToken));
    }

    private static HttpRequestMessage CreateGetUserRequest(Guid userId, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}
