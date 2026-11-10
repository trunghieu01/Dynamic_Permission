using DynamicPermission.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Application.Services;

public sealed class PermissionService : IPermissionService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPermissionCache _permissionCache;

    public PermissionService(IApplicationDbContext dbContext, IPermissionCache permissionCache)
    {
        _dbContext = dbContext;
        _permissionCache = permissionCache;
    }

    public async Task<HashSet<string>> GetUserEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var permissionCodes = await _permissionCache.GetOrCreateAsync(
            userId,
            token => ResolveRoleIdAsync(userId, token),
            token => LoadPermissionsAsync(userId, token),
            cancellationToken);

        return new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);
    }

    private Task<Guid?> ResolveRoleIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (Guid?)user.RoleId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<PermissionCacheSnapshot> LoadPermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.Role)
                .ThenInclude(role => role.RolePermissions)
                    .ThenInclude(rolePermission => rolePermission.Permission)
            .Include(candidate => candidate.UserPermissions)
                .ThenInclude(userPermission => userPermission.Permission)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        var effectivePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (user is null)
        {
            return new PermissionCacheSnapshot(null, Array.Empty<string>());
        }

        if (user.IsActive && user.Role?.IsActive == true)
        {
            foreach (var rolePermission in user.Role.RolePermissions)
            {
                if (rolePermission.Permission.IsActive)
                {
                    effectivePermissions.Add(rolePermission.Permission.Code);
                }
            }

            foreach (var userPermission in user.UserPermissions.Where(permission =>
                         permission.IsGranted && permission.Permission.IsActive))
            {
                effectivePermissions.Add(userPermission.Permission.Code);
            }

            foreach (var userPermission in user.UserPermissions.Where(permission => !permission.IsGranted))
            {
                effectivePermissions.Remove(userPermission.Permission.Code);
            }
        }

        return new PermissionCacheSnapshot(user.RoleId, effectivePermissions);
    }
}
