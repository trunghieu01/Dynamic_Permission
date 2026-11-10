namespace DynamicPermission.Application.Models.Admin.Users;

public sealed record UserResponse(
    Guid Id,
    string Username,
    string Email,
    string FullName,
    bool IsActive,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
