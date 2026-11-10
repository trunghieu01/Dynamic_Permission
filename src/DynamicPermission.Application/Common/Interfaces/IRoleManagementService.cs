using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;

namespace DynamicPermission.Application.Common.Interfaces;

public interface IRoleManagementService
{
    Task<PagedResponse<RoleResponse>> GetRolesAsync(RoleListQuery query, CancellationToken cancellationToken);
    Task<RoleResponse?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken);
    Task<RoleResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleResponse?> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleResponse?> DeactivateRoleAsync(Guid roleId, CancellationToken cancellationToken);
    Task<RoleResponse?> SetRoleStatusAsync(Guid roleId, bool isActive, CancellationToken cancellationToken);
    Task<RoleResponse?> SetRoleStatusAsync(Guid roleId, UpdateRoleStatusRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<PermissionResponse>?> GetRolePermissionsAsync(Guid roleId, PermissionListQuery query, CancellationToken cancellationToken);
    Task<bool> AssignPermissionAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken);
    Task<bool> RemovePermissionAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken);
}
