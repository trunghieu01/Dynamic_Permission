using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;
using DynamicPermission.Application.Models.Admin.Users;

namespace DynamicPermission.Application.Common.Interfaces;

public interface IUserManagementService
{
    Task<PagedResponse<UserResponse>> GetUsersAsync(UserListQuery query, CancellationToken cancellationToken);
    Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserResponse?> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<UserResponse?> SetUserStatusAsync(Guid userId, UpdateUserStatusRequest request, CancellationToken cancellationToken);
    Task<UserResponse?> AssignRoleAsync(Guid userId, AssignUserRoleRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<RoleResponse>?> GetUserRolesAsync(Guid userId, RoleListQuery query, CancellationToken cancellationToken);
    Task<PagedResponse<PermissionResponse>?> GetEffectivePermissionsAsync(Guid userId, PermissionListQuery query, CancellationToken cancellationToken);
}
