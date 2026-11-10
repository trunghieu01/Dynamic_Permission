namespace DynamicPermission.Application.Common.Interfaces;

public interface IPermissionCache
{
    Task<IReadOnlySet<string>> GetOrCreateAsync(
        Guid userId,
        Func<CancellationToken, Task<Guid?>> roleResolver,
        Func<CancellationToken, Task<PermissionCacheSnapshot>> loader,
        CancellationToken cancellationToken);

    void InvalidateUser(Guid userId);

    void InvalidateRole(Guid roleId);

    void InvalidateAll();
}

public sealed record PermissionCacheSnapshot(Guid? RoleId, IReadOnlyCollection<string> PermissionCodes);
