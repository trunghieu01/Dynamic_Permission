using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DynamicPermission.UnitTests.Infrastructure.Authentication;

public class TokenServiceTests
{
    private readonly JwtSettings _jwtSettings;
    private readonly TokenService _tokenService;

    public TokenServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            SecretKey = "TestSecretKey_ForUnitTestingOnly_MinLength32Chars!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        var optionsMock = Options.Create(_jwtSettings);
        _tokenService = new TokenService(optionsMock);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwt_WithExpectedClaims_AndNoPermissions()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "johndoe",
            Email = "john@example.com"
        };
        var roleCode = "ADMIN";

        // Act
        var (token, expiresIn) = _tokenService.GenerateAccessToken(user, roleCode);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        expiresIn.Should().Be(15 * 60);

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Issuer.Should().Be(_jwtSettings.Issuer);
        jwtToken.Audiences.Should().Contain(_jwtSettings.Audience);

        // Verify Claims (JwtSecurityToken serializes ClaimTypes.NameIdentifier as 'nameid', ClaimTypes.Role as 'role')
        jwtToken.Claims.Should().Contain(c => c.Type == "sub" && c.Value == user.Id.ToString());
        jwtToken.Claims.Should().Contain(c => (c.Type == "nameid" || c.Type == ClaimTypes.NameIdentifier) && c.Value == user.Id.ToString());
        jwtToken.Claims.Should().Contain(c => (c.Type == "unique_name" || c.Type == ClaimTypes.Name) && c.Value == user.Username);
        jwtToken.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwtToken.Claims.Should().Contain(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == roleCode);

        // CRITICAL: Permissions MUST NOT be in the token claims
        jwtToken.Claims.Should().NotContain(c => c.Type.Equals("permission", StringComparison.OrdinalIgnoreCase));
        jwtToken.Claims.Should().NotContain(c => c.Type.Equals("permissions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnValidBase64Token_WithCorrectExpiration()
    {
        // Act
        var (token, expiresAt) = _tokenService.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        // Verify it is a valid Base64 string of 64 bytes
        var bytes = Convert.FromBase64String(token);
        bytes.Length.Should().Be(64);

        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        expiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ShouldExtractClaims_EvenWhenExpired()
    {
        // Arrange: Generate an expired token (NotBefore must be earlier than Expires)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expectedUserId = Guid.NewGuid().ToString();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, expectedUserId),
            new Claim(ClaimTypes.Name, "expired_user")
        };

        var expiredTokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-30),
            Expires = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var expiredJwt = handler.WriteToken(handler.CreateToken(expiredTokenDescriptor));

        // Act
        var principal = _tokenService.GetPrincipalFromExpiredToken(expiredJwt);

        // Assert
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be(expectedUserId);
        principal.FindFirst(ClaimTypes.Name)?.Value.Should().Be("expired_user");
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ShouldReturnNull_WhenTokenIsTamperedOrInvalid()
    {
        // Arrange
        var tamperedToken = "header.payload.invalidSignature123";

        // Act
        var principal = _tokenService.GetPrincipalFromExpiredToken(tamperedToken);

        // Assert
        principal.Should().BeNull();
    }
}
