using System.Security.Claims;
using DynamicPermission.Domain.Entities;

namespace DynamicPermission.Application.Common.Interfaces;

public interface ITokenService
{
    (string Token, int ExpiresIn) GenerateAccessToken(User user, string roleCode);
    (string Token, DateTimeOffset ExpiresAt) GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
