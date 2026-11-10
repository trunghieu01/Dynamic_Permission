using DynamicPermission.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DynamicPermission.Infrastructure.Authorization;

public sealed class MemoryPermissionCache : IPermissionCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private readonly IMemoryCache _memoryCache;
    private long _version;

    public MemoryPermissionCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public long Version => Interlocked.Read(ref _version);

    public bool TryGet(Guid userId, long version, out IReadOnlySet<string> permissions)
    {
        if (Version == version
            && _memoryCache.TryGetValue(GetCacheKey(userId, version), out string[]? cachedPermissions)
            && cachedPermissions is not null
            && Version == version)
        {
            permissions = new HashSet<string>(cachedPermissions, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    public void Set(Guid userId, long version, IReadOnlyCollection<string> permissions)
    {
        if (Version != version)
        {
            return;
        }

        _memoryCache.Set(GetCacheKey(userId, version), permissions.ToArray(), CacheDuration);
    }

    public void InvalidateAll()
    {
        Interlocked.Increment(ref _version);
    }

    private static string GetCacheKey(Guid userId, long version) => $"user_perms_{version}_{userId}";
}
