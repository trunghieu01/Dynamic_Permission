using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Common.Exceptions;
using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Application.Services.Admin;

public sealed class RoleManagementService : IRoleManagementService
{
    private readonly IApplicationDbContext _dbContext;

    public RoleManagementService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<RoleResponse>> GetRolesAsync(
        RoleListQuery query,
        CancellationToken cancellationToken)
    {
        var roles = _dbContext.Roles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            roles = roles.Where(role => role.Code.Contains(search) || role.Name.Contains(search));
        }

        if (query.IsActive.HasValue)
        {
            roles = roles.Where(role => role.IsActive == query.IsActive.Value);
        }

        roles = AdminQueryHelper.ApplyRoleSort(roles, query);
        return await AdminQueryHelper.ToPagedResponseAsync(roles, query, MapRole, cancellationToken);
    }

    public async Task<RoleResponse?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == roleId, cancellationToken);
        return role is null ? null : MapRole(role);
    }

    public async Task<RoleResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (await _dbContext.Roles.AnyAsync(role => role.Code.ToUpper() == code.ToUpper(), cancellationToken))
        {
            throw new ConflictException("Role code is already in use.");
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            IsActive = true
        };
        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapRole(role);
    }

    public async Task<RoleResponse?> UpdateRoleAsync(
        Guid roleId,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.SingleOrDefaultAsync(candidate => candidate.Id == roleId, cancellationToken);
        if (role is null)
        {
            return null;
        }

        var code = request.Code.Trim();
        if (await _dbContext.Roles.AnyAsync(candidate =>
                candidate.Id != roleId && candidate.Code.ToUpper() == code.ToUpper(),
                cancellationToken))
        {
            throw new ConflictException("Role code is already in use.");
        }

        role.Code = code;
        role.Name = request.Name.Trim();
        role.Description = NormalizeOptional(request.Description);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapRole(role);
    }

    public async Task<RoleResponse?> DeactivateRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        return await SetRoleStatusAsync(roleId, isActive: false, cancellationToken);
    }

    public async Task<RoleResponse?> SetRoleStatusAsync(
        Guid roleId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.SingleOrDefaultAsync(candidate => candidate.Id == roleId, cancellationToken);
        if (role is null)
        {
            return null;
        }

        role.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapRole(role);
    }

    public Task<RoleResponse?> SetRoleStatusAsync(
        Guid roleId,
        UpdateRoleStatusRequest request,
        CancellationToken cancellationToken)
    {
        var isActive = request.IsActive
            ?? throw new ArgumentException("IsActive is required.", nameof(request));
        return SetRoleStatusAsync(roleId, isActive, cancellationToken);
    }

    public async Task<PagedResponse<PermissionResponse>?> GetRolePermissionsAsync(
        Guid roleId,
        PermissionListQuery query,
        CancellationToken cancellationToken)
    {
        var roleExists = await _dbContext.Roles.AsNoTracking()
            .AnyAsync(role => role.Id == roleId, cancellationToken);
        if (!roleExists)
        {
            return null;
        }

        var permissions = _dbContext.RolePermissions.AsNoTracking()
            .Where(rolePermission => rolePermission.RoleId == roleId)
            .Select(rolePermission => rolePermission.Permission);
        permissions = AdminQueryHelper.FilterPermissions(permissions, query);
        permissions = AdminQueryHelper.ApplyPermissionSort(permissions, query);
        return await AdminQueryHelper.ToPagedResponseAsync(
            permissions,
            query,
            PermissionManagementService.MapPermission,
            cancellationToken);
    }

    public async Task<bool> AssignPermissionAsync(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.SingleOrDefaultAsync(candidate => candidate.Id == roleId, cancellationToken);
        var permission = await _dbContext.Permissions.SingleOrDefaultAsync(
            candidate => candidate.Id == permissionId,
            cancellationToken);
        if (role is null || permission is null)
        {
            return false;
        }

        if (!role.IsActive || !permission.IsActive)
        {
            throw new ConflictException("Only active roles and permissions can be assigned.");
        }

        var exists = await _dbContext.RolePermissions.AnyAsync(
            assignment => assignment.RoleId == roleId && assignment.PermissionId == permissionId,
            cancellationToken);
        if (exists)
        {
            throw new ConflictException("Permission is already assigned to this role.");
        }

        _dbContext.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemovePermissionAsync(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.RolePermissions.SingleOrDefaultAsync(
            candidate => candidate.RoleId == roleId && candidate.PermissionId == permissionId,
            cancellationToken);
        if (assignment is null)
        {
            return false;
        }

        _dbContext.RolePermissions.Remove(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static RoleResponse MapRole(Role role) => new(
        role.Id,
        role.Code,
        role.Name,
        role.Description,
        role.IsActive,
        role.CreatedAt,
        role.UpdatedAt);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
