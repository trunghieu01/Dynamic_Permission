namespace DynamicPermission.Infrastructure.Authorization;

public sealed class PermissionCacheInvalidation
{
    public HashSet<Guid> UserIds { get; } = new();
    public HashSet<Guid> RoleIds { get; } = new();
    public bool InvalidateAll { get; set; }

    public bool IsEmpty => !InvalidateAll && UserIds.Count == 0 && RoleIds.Count == 0;

    public void Merge(PermissionCacheInvalidation other)
    {
        InvalidateAll |= other.InvalidateAll;
        UserIds.UnionWith(other.UserIds);
        RoleIds.UnionWith(other.RoleIds);
    }

    public void Apply(Application.Common.Interfaces.IPermissionCache cache)
    {
        if (InvalidateAll)
        {
            cache.InvalidateAll();
            return;
        }

        foreach (var userId in UserIds)
        {
            cache.InvalidateUser(userId);
        }

        foreach (var roleId in RoleIds)
        {
            cache.InvalidateRole(roleId);
        }
    }
}
