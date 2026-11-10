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
        var cacheVersion = _permissionCache.Version;
        if (_permissionCache.TryGet(userId, cacheVersion, out var cachedPermissions))
        {
            return new HashSet<string>(cachedPermissions, StringComparer.OrdinalIgnoreCase);
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.Role)
                .ThenInclude(role => role.RolePermissions)
                    .ThenInclude(rolePermission => rolePermission.Permission)
            .Include(candidate => candidate.UserPermissions)
                .ThenInclude(userPermission => userPermission.Permission)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        var effectivePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (user is not null && user.IsActive)
        {
            if (user.Role?.IsActive == true)
            {
                foreach (var rolePermission in user.Role.RolePermissions)
                {
                    effectivePermissions.Add(rolePermission.Permission.Code);
                }
            }

            foreach (var userPermission in user.UserPermissions.Where(permission => permission.IsGranted))
            {
                effectivePermissions.Add(userPermission.Permission.Code);
            }

            foreach (var userPermission in user.UserPermissions.Where(permission => !permission.IsGranted))
            {
                effectivePermissions.Remove(userPermission.Permission.Code);
            }
        }

        if (_permissionCache.Version == cacheVersion)
        {
            _permissionCache.Set(userId, cacheVersion, effectivePermissions);
        }

        return effectivePermissions;
    }
}
