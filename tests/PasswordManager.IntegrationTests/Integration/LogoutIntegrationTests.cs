using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Application.Interfaces.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;
using Xunit;

namespace PasswordManager.IntegrationTests.Integration;

public class LogoutIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public LogoutIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    private async Task<HttpResponseMessage> RegisterUserViaApiAsync(string email, string password)
    {
        var request = new RegisterUserRequest(email, password, password);
        return await _client.PostAsJsonAsync("/api/auth/register", request);
    }

    private async Task<string> LoginAndGetRefreshTokenAsync(string email, string password)
    {
        var loginPayload = new LoginRequest(email, password);
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        Assert.True(loginResponse.Headers.Contains("Set-Cookie"));
        var setCookieHeader = loginResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(setCookieHeader);

        var rawToken = setCookieHeader.Split(';')[0]["refreshToken=".Length..];
        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        return rawToken;
    }

    [Fact]
    public async Task Registration_Then_Login_Then_Logout_FullLifecycle_Succeeds_WithDatabaseRevocation_AndCookieDeletion()
    {
        // 1. Account Creation (Registration)
        const string email = "logout.lifecycle.success@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";

        var registerResponse = await RegisterUserViaApiAsync(email, password);
        Assert.True(
            registerResponse.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Registration failed with unexpected status: {registerResponse.StatusCode}");

        // 2. Login to establish real refresh session
        var rawRefreshToken = await LoginAndGetRefreshTokenAsync(email, password);

        // 3. Database Verification before logout: Active RefreshToken exists
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var expectedHash = tokenService.HashRefreshToken(rawRefreshToken);

            var preLogoutToken = await db.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.TokenHash == expectedHash);

            Assert.NotNull(preLogoutToken);
            Assert.Equal(email, preLogoutToken.User.Email);
            Assert.True(preLogoutToken.IsActive);
            Assert.False(preLogoutToken.IsRevoked);
            Assert.Null(preLogoutToken.RevokedAt);
            Assert.True(preLogoutToken.ExpiresAt > DateTime.UtcNow);
        }

        // 4. Execute Real HTTP POST /api/auth/logout with the refresh-token cookie
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"refreshToken={rawRefreshToken}");

        var logoutResponse = await _client.SendAsync(logoutRequest);

        // 5. Verify HTTP 204 No Content with empty body
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        var body = await logoutResponse.Content.ReadAsStringAsync();
        Assert.Empty(body);

        // 6. Verify Cookie Deletion Header
        Assert.True(logoutResponse.Headers.Contains("Set-Cookie"));
        var deleteCookieHeader = logoutResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(deleteCookieHeader);

        Assert.Contains("path=/api/auth", deleteCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", deleteCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", deleteCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            deleteCookieHeader.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            deleteCookieHeader.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));

        // 7. Verify Database State after logout: Session is revoked, User still exists
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var expectedHash = tokenService.HashRefreshToken(rawRefreshToken);

            var postLogoutToken = await db.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.TokenHash == expectedHash);

            Assert.NotNull(postLogoutToken);
            Assert.True(postLogoutToken.IsRevoked);
            Assert.False(postLogoutToken.IsActive);
            Assert.NotNull(postLogoutToken.RevokedAt);
            Assert.True(postLogoutToken.RevokedAt <= DateTime.UtcNow);

            // User record still exists and was not deleted
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.NotNull(user);
        }
    }

    [Fact]
    public async Task Logout_CookieDeletion_AdheresToContract_AndEnvironmentAwareSecurePolicy()
    {
        // 1. HTTP Request (Development / Non-HTTPS environment): Secure flag is omitted
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/auth/logout");
        var httpResponse = await _client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.NoContent, httpResponse.StatusCode);
        var httpBody = await httpResponse.Content.ReadAsStringAsync();
        Assert.Empty(httpBody);

        Assert.True(httpResponse.Headers.Contains("Set-Cookie"));
        var httpSetCookie = httpResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(httpSetCookie);

        Assert.Contains("path=/api/auth", httpSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", httpSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", httpSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            httpSetCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            httpSetCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("secure", httpSetCookie, StringComparison.OrdinalIgnoreCase);

        // 2. HTTPS Request: Request.IsHttps is true, so Secure flag is explicitly appended
        var httpsRequest = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/auth/logout");
        var httpsResponse = await _client.SendAsync(httpsRequest);

        Assert.Equal(HttpStatusCode.NoContent, httpsResponse.StatusCode);
        var httpsBody = await httpsResponse.Content.ReadAsStringAsync();
        Assert.Empty(httpsBody);

        Assert.True(httpsResponse.Headers.Contains("Set-Cookie"));
        var httpsSetCookie = httpsResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(httpsSetCookie);

        Assert.Contains("path=/api/auth", httpsSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", httpsSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", httpsSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            httpsSetCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            httpsSetCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("secure", httpsSetCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_MultipleSessions_RevokesOnlyTargetSession_LeavingOtherSessionsActive()
    {
        // 1. Register user
        const string email = "logout.session.isolation.flow@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";
        await RegisterUserViaApiAsync(email, password);

        // 2. Establish two independent sessions (e.g. Browser A and Browser B)
        var rawTokenA = await LoginAndGetRefreshTokenAsync(email, password);
        var rawTokenB = await LoginAndGetRefreshTokenAsync(email, password);
        Assert.NotEqual(rawTokenA, rawTokenB);

        // 3. Confirm both sessions exist and are active in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hashA = tokenService.HashRefreshToken(rawTokenA);
            var hashB = tokenService.HashRefreshToken(rawTokenB);

            var tokenA = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashA);
            var tokenB = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashB);

            Assert.True(tokenA.IsActive);
            Assert.True(tokenB.IsActive);
        }

        // 4. Logout Session A only
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"refreshToken={rawTokenA}");

        var logoutResponse = await _client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // 5. Assert: Session A is revoked, Session B is STILL active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hashA = tokenService.HashRefreshToken(rawTokenA);
            var hashB = tokenService.HashRefreshToken(rawTokenB);

            var tokenA = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashA);
            var tokenB = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hashB);

            // Session A revoked
            Assert.True(tokenA.IsRevoked);
            Assert.False(tokenA.IsActive);
            Assert.NotNull(tokenA.RevokedAt);

            // Session B remains active (single-session isolation, NO global logout)
            Assert.False(tokenB.IsRevoked);
            Assert.True(tokenB.IsActive);
            Assert.Null(tokenB.RevokedAt);

            // User entity remains untouched
            var user = await db.Users.FirstAsync(u => u.Email == email);
            Assert.NotNull(user);
        }
    }

    [Fact]
    public async Task Logout_WithoutCookie_Returns204NoContent_WithoutModifyingDatabase()
    {
        // 1. Setup user with active session
        const string email = "logout.nocookie.integration@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";
        await RegisterUserViaApiAsync(email, password);
        var rawToken = await LoginAndGetRefreshTokenAsync(email, password);

        // 2. Call logout with NO cookie header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        var response = await _client.SendAsync(request);

        // 3. Verify 204 No Content with empty body
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);

        // 4. Verify cookie deletion header was still returned
        Assert.True(response.Headers.Contains("Set-Cookie"));

        // 5. Verify database session remains active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hash = tokenService.HashRefreshToken(rawToken);

            var session = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hash);
            Assert.True(session.IsActive);
            Assert.Null(session.RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_AlreadyRevokedSession_Returns204NoContent_IdempotentlyPreservingTimestamp()
    {
        // 1. Setup active session
        const string email = "logout.alreadyrevoked.flow@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";
        await RegisterUserViaApiAsync(email, password);
        var rawToken = await LoginAndGetRefreshTokenAsync(email, password);

        // 2. First logout: revokes session
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request1.Headers.Add("Cookie", $"refreshToken={rawToken}");
        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.NoContent, response1.StatusCode);

        DateTime? firstRevocationTime;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hash = tokenService.HashRefreshToken(rawToken);

            var session = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hash);
            firstRevocationTime = session.RevokedAt;
            Assert.NotNull(firstRevocationTime);
        }

        // 3. Second logout with the exact same token: idempotent operation
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request2.Headers.Add("Cookie", $"refreshToken={rawToken}");
        var response2 = await _client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.NoContent, response2.StatusCode);

        // 4. Verify RevokedAt timestamp was preserved (not overwritten with new time)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var hash = tokenService.HashRefreshToken(rawToken);

            var session = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hash);
            Assert.Equal(firstRevocationTime, session.RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_ExpiredSession_Returns204NoContent_DoesNotReactivate()
    {
        // 1. Create user and manually insert an expired session
        const string email = "logout.expired.integration@vaultx.local";
        const string password = "VaultX@IntegrationTest2026!";
        await RegisterUserViaApiAsync(email, password);

        const string rawExpiredToken = "raw-expired-integration-token-value-55555";
        string tokenHash;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var user = await db.Users.FirstAsync(u => u.Email == email);

            tokenHash = tokenService.HashRefreshToken(rawExpiredToken);

            var session = new RefreshToken(user.Id, tokenHash, DateTime.UtcNow.AddMinutes(5));
            await db.RefreshTokens.AddAsync(session);
            await db.SaveChangesAsync();

            // Force expiration into the past directly in the database
            typeof(RefreshToken).GetProperty(nameof(RefreshToken.ExpiresAt))!
                .SetValue(session, DateTime.UtcNow.AddDays(-2));
            await db.SaveChangesAsync();
        }

        // 2. Call logout with the expired session cookie
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={rawExpiredToken}");
        var response = await _client.SendAsync(request);

        // 3. Verify HTTP 204 No Content
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // 4. Verify in DB: Session remains expired, not reactivated, RevokedAt unchanged
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == tokenHash);

            Assert.True(session.IsExpired);
            Assert.False(session.IsActive);
            Assert.Null(session.RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_PreservesUserData_PasswordHash_AndUnrelatedRecords()
    {
        // 1. Register target user and an unrelated user
        const string targetEmail = "logout.datapreservation.target@vaultx.local";
        const string targetPassword = "VaultX@TargetPass2026!";
        await RegisterUserViaApiAsync(targetEmail, targetPassword);

        const string unrelatedEmail = "logout.datapreservation.unrelated@vaultx.local";
        const string unrelatedPassword = "VaultX@UnrelatedPass2026!";
        await RegisterUserViaApiAsync(unrelatedEmail, unrelatedPassword);

        // 2. Establish sessions for both users
        var targetRawToken = await LoginAndGetRefreshTokenAsync(targetEmail, targetPassword);
        var unrelatedRawToken = await LoginAndGetRefreshTokenAsync(unrelatedEmail, unrelatedPassword);

        byte[] targetPasswordHash;
        Guid targetUserId;
        byte[] unrelatedPasswordHash;
        Guid unrelatedUserId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var targetUser = await db.Users.FirstAsync(u => u.Email == targetEmail);
            targetUserId = targetUser.Id;
            targetPasswordHash = (byte[])targetUser.PasswordHash.Clone();

            var unrelatedUser = await db.Users.FirstAsync(u => u.Email == unrelatedEmail);
            unrelatedUserId = unrelatedUser.Id;
            unrelatedPasswordHash = (byte[])unrelatedUser.PasswordHash.Clone();
        }

        int totalUsersBeforeLogout;
        int totalSessionsBeforeLogout;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            totalUsersBeforeLogout = await db.Users.CountAsync();
            totalSessionsBeforeLogout = await db.RefreshTokens.CountAsync();
        }

        // 3. Logout target user
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"refreshToken={targetRawToken}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // 4. Verify Data Integrity:
        // - Target User still exists with identical ID and PasswordHash
        // - Target RefreshToken row still exists (updated to revoked, NOT deleted)
        // - Unrelated User still exists with identical ID and PasswordHash
        // - Unrelated RefreshToken remains ACTIVE
        // - Total user count and session count remain unchanged (no records deleted)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

            var targetUser = await db.Users.FirstOrDefaultAsync(u => u.Email == targetEmail);
            Assert.NotNull(targetUser);
            Assert.Equal(targetUserId, targetUser.Id);
            Assert.Equal(targetPasswordHash, targetUser.PasswordHash);

            var targetHash = tokenService.HashRefreshToken(targetRawToken);
            var targetSession = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == targetHash);
            Assert.True(targetSession.IsRevoked);
            Assert.False(targetSession.IsActive);
            Assert.NotNull(targetSession.RevokedAt);

            var unrelatedUser = await db.Users.FirstOrDefaultAsync(u => u.Email == unrelatedEmail);
            Assert.NotNull(unrelatedUser);
            Assert.Equal(unrelatedUserId, unrelatedUser.Id);
            Assert.Equal(unrelatedPasswordHash, unrelatedUser.PasswordHash);

            var unrelatedHash = tokenService.HashRefreshToken(unrelatedRawToken);
            var unrelatedSession = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == unrelatedHash);
            Assert.False(unrelatedSession.IsRevoked);
            Assert.True(unrelatedSession.IsActive);
            Assert.Null(unrelatedSession.RevokedAt);

            var totalUsersAfterLogout = await db.Users.CountAsync();
            var totalSessionsAfterLogout = await db.RefreshTokens.CountAsync();
            Assert.Equal(totalUsersBeforeLogout, totalUsersAfterLogout);
            Assert.Equal(totalSessionsBeforeLogout, totalSessionsAfterLogout);
        }
    }
}
