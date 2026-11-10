using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;

namespace DynamicPermission.Application.Common.Interfaces;

public interface IPermissionManagementService
{
    Task<PagedResponse<PermissionResponse>> GetPermissionsAsync(PermissionListQuery query, CancellationToken cancellationToken);
    Task<PermissionResponse?> GetPermissionAsync(Guid permissionId, CancellationToken cancellationToken);
    Task<PermissionResponse> CreatePermissionAsync(CreatePermissionRequest request, CancellationToken cancellationToken);
    Task<PermissionResponse?> UpdatePermissionAsync(Guid permissionId, UpdatePermissionRequest request, CancellationToken cancellationToken);
    Task<PermissionResponse?> SetPermissionStatusAsync(Guid permissionId, UpdatePermissionStatusRequest request, CancellationToken cancellationToken);
}
