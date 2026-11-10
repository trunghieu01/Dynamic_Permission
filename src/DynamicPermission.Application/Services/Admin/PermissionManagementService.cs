using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Common.Exceptions;
using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Application.Services.Admin;

public sealed class PermissionManagementService : IPermissionManagementService
{
    private readonly IApplicationDbContext _dbContext;

    public PermissionManagementService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<PermissionResponse>> GetPermissionsAsync(
        PermissionListQuery query,
        CancellationToken cancellationToken)
    {
        var permissions = AdminQueryHelper.FilterPermissions(
            _dbContext.Permissions.AsNoTracking(),
            query);
        permissions = AdminQueryHelper.ApplyPermissionSort(permissions, query);
        return await AdminQueryHelper.ToPagedResponseAsync(
            permissions,
            query,
            MapPermission,
            cancellationToken);
    }

    public async Task<PermissionResponse?> GetPermissionAsync(Guid permissionId, CancellationToken cancellationToken)
    {
        var permission = await _dbContext.Permissions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == permissionId, cancellationToken);
        return permission is null ? null : MapPermission(permission);
    }

    public async Task<PermissionResponse> CreatePermissionAsync(
        CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (await _dbContext.Permissions.AnyAsync(
                permission => permission.Code.ToUpper() == code.ToUpper(),
                cancellationToken))
        {
            throw new ConflictException("Permission code is already in use.");
        }

        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Module = request.Module.Trim(),
            Action = request.Action.Trim(),
            Description = NormalizeOptional(request.Description),
            IsActive = true
        };
        _dbContext.Permissions.Add(permission);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapPermission(permission);
    }

    public async Task<PermissionResponse?> UpdatePermissionAsync(
        Guid permissionId,
        UpdatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _dbContext.Permissions.SingleOrDefaultAsync(
            candidate => candidate.Id == permissionId,
            cancellationToken);
        if (permission is null)
        {
            return null;
        }

        var code = request.Code.Trim();
        if (await _dbContext.Permissions.AnyAsync(candidate =>
                candidate.Id != permissionId && candidate.Code.ToUpper() == code.ToUpper(),
                cancellationToken))
        {
            throw new ConflictException("Permission code is already in use.");
        }

        permission.Code = code;
        permission.Name = request.Name.Trim();
        permission.Module = request.Module.Trim();
        permission.Action = request.Action.Trim();
        permission.Description = NormalizeOptional(request.Description);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapPermission(permission);
    }

    public async Task<PermissionResponse?> SetPermissionStatusAsync(
        Guid permissionId,
        UpdatePermissionStatusRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _dbContext.Permissions.SingleOrDefaultAsync(
            candidate => candidate.Id == permissionId,
            cancellationToken);
        if (permission is null)
        {
            return null;
        }

        permission.IsActive = request.IsActive
            ?? throw new ArgumentException("IsActive is required.", nameof(request));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapPermission(permission);
    }

    internal static PermissionResponse MapPermission(Permission permission) => new(
        permission.Id,
        permission.Code,
        permission.Name,
        permission.Module,
        permission.Action,
        permission.Description,
        permission.IsActive,
        permission.CreatedAt,
        permission.UpdatedAt);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
