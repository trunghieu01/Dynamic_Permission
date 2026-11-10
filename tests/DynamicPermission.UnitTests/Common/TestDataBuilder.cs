using DynamicPermission.Domain.Entities;

namespace DynamicPermission.UnitTests.Common;

public static class TestDataBuilder
{
    public static Role CreateRole(string code = "ADMIN", string name = "Quản trị viên", bool isActive = true)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = "Mô tả vai trò",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "System"
        };
    }

    public static User CreateUser(Guid roleId, string username = "testuser", string email = "test@example.com", string passwordHash = "hashed_password", bool isActive = true)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            Username = username,
            Email = email,
            FullName = "Test User",
            PasswordHash = passwordHash,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "System"
        };
    }

    public static RefreshToken CreateRefreshToken(Guid userId, string token = "sample_refresh_token", bool isRevoked = false, int daysValid = 7)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(daysValid),
            IsRevoked = isRevoked,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "System"
        };
    }
}
