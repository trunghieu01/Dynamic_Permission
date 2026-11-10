namespace DynamicPermission.Application.Models.Admin.Permissions;

public sealed record PermissionResponse(
    Guid Id,
    string Code,
    string Name,
    string Module,
    string Action,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
