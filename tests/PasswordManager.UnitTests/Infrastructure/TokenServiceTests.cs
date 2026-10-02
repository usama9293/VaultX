using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PasswordManager.Application.Common.Security;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Services;
using Xunit;

namespace PasswordManager.UnitTests.Infrastructure;

public class TokenServiceTests
{
    private readonly JwtSettings _jwtSettings;
    private readonly TokenService _tokenService;

    public TokenServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            Issuer = "VaultX.Test.API",
            Audience = "VaultX.Test.Client",
            SecretKey = "SuperSecretKeyForVaultXTestingPurposesOnly2026!",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        var options = Options.Create(_jwtSettings);
        _tokenService = new TokenService(options);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwtWithExpectedClaims()
    {
        var passwordHash = Encoding.UTF8.GetBytes("$pbkdf2-sha256$i=100000$test");
        var user = new User("user@example.com", passwordHash);

        var (tokenString, expiresAt) = _tokenService.GenerateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(tokenString));
        Assert.True(expiresAt > DateTime.UtcNow);
        Assert.True(expiresAt <= DateTime.UtcNow.AddMinutes(16));

        // Validate token structure and claims
        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = handler.ValidateToken(tokenString, validationParams, out var validatedToken);
        Assert.NotNull(validatedToken);

        var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Assert.Equal(user.Id.ToString(), subClaim);

        var emailClaim = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        Assert.Equal("user@example.com", emailClaim);

        var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        Assert.NotNull(jtiClaim);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsRawTokenAndMatchingHash()
    {
        var (rawToken, tokenHash, expiresAt) = _tokenService.GenerateRefreshToken();

        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenHash));
        Assert.NotEqual(rawToken, tokenHash);
        Assert.True(expiresAt > DateTime.UtcNow.AddDays(6));

        // Verify SHA-256 hash
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        Assert.Equal(expectedHash, tokenHash);
    }

    [Fact]
    public void HashRefreshToken_IsDeterministic()
    {
        const string rawToken = "sample-raw-refresh-token-123456";

        var hash1 = _tokenService.HashRefreshToken(rawToken);
        var hash2 = _tokenService.HashRefreshToken(rawToken);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 256 bits = 64 hex characters
    }

    [Fact]
    public void Constructor_SecretKeyTooShort_ThrowsInvalidOperationException()
    {
        var shortKeySettings = new JwtSettings
        {
            SecretKey = "too-short"
        };

        Assert.Throws<InvalidOperationException>(() => new TokenService(Options.Create(shortKeySettings)));
    }

    [Fact]
    public void GenerateAccessToken_DoesNotIncludeSensitiveClaims()
    {
        var passwordHash = Encoding.UTF8.GetBytes("$pbkdf2-sha256$i=100000$s=saltsalt$h=hashhash");
        var user = new User("claims.security@vaultx.local", passwordHash);

        var (tokenString, _) = _tokenService.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        var prohibitedClaims = new[] { "password", "passwordHash", "tokenHash", "refreshToken", "rawRefreshToken", "role", "isAdmin" };
        foreach (var claim in jwt.Claims)
        {
            Assert.DoesNotContain(claim.Type, prohibitedClaims);
            Assert.DoesNotContain("$pbkdf2", claim.Value);
        }
    }
}
