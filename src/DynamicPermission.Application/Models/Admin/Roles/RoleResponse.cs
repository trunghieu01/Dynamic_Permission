namespace DynamicPermission.Application.Models.Admin.Roles;

public sealed record RoleResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
