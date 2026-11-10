using DynamicPermission.Application.Models.Admin;

namespace DynamicPermission.Application.Models.Admin.Users;

public sealed record AssignUserRoleRequest
{
    [NotEmptyGuid]
    public Guid RoleId { get; init; }
}
