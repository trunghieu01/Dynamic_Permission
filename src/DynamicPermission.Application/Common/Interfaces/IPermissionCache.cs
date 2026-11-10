namespace DynamicPermission.Application.Common.Interfaces;

public interface IPermissionCache
{
    long Version { get; }

    bool TryGet(Guid userId, long version, out IReadOnlySet<string> permissions);

    void Set(Guid userId, long version, IReadOnlyCollection<string> permissions);

    void InvalidateAll();
}
