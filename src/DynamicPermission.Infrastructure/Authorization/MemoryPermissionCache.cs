using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DynamicPermission.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DynamicPermission.Infrastructure.Authorization;

public sealed class MemoryPermissionCache : IPermissionCache
{
    private const string CacheKeyPrefix = "dynamic-permission:v1:user";
    private static readonly Meter Meter = new("DynamicPermission.Authorization");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("dynamic_permission_cache_hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("dynamic_permission_cache_misses");
    private static readonly Counter<long> CacheLoads = Meter.CreateCounter<long>("dynamic_permission_cache_loads");
    private static readonly Counter<long> CacheLoadFailures = Meter.CreateCounter<long>("dynamic_permission_cache_load_failures");
    private static readonly Counter<long> CacheInvalidations = Meter.CreateCounter<long>("dynamic_permission_cache_invalidations");
    private static readonly Histogram<double> CacheLoadDuration = Meter.CreateHistogram<double>(
        "dynamic_permission_cache_load_duration",
        unit: "ms");
    private readonly IMemoryCache _memoryCache;
    private readonly TimeSpan _expiration;
    private readonly ConcurrentDictionary<Guid, long> _userGenerations = new();
    private readonly ConcurrentDictionary<Guid, long> _roleGenerations = new();
    private readonly KeyedAsyncLock<Guid> _userLocks = new();
    private long _globalGeneration;

    public MemoryPermissionCache(IMemoryCache memoryCache, IOptions<PermissionCacheOptions> options)
        : this(memoryCache, TimeSpan.FromSeconds(options.Value.ExpirationSeconds))
    {
    }

    public MemoryPermissionCache(IMemoryCache memoryCache, TimeSpan expiration)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        if (expiration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiration), "Cache expiration must be positive.");
        }

        _memoryCache = memoryCache;
        _expiration = expiration;
    }

    public async Task<IReadOnlySet<string>> GetOrCreateAsync(
        Guid userId,
        Func<CancellationToken, Task<Guid?>> roleResolver,
        Func<CancellationToken, Task<PermissionCacheSnapshot>> loader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleResolver);
        ArgumentNullException.ThrowIfNull(loader);

        if (TryGetCurrent(userId, out var cachedPermissions))
        {
            return cachedPermissions;
        }

        using (await _userLocks.AcquireAsync(userId, cancellationToken))
        {
            while (true)
            {
                if (TryGetCurrent(userId, out cachedPermissions))
                {
                    return cachedPermissions;
                }

                var userGeneration = GetGeneration(_userGenerations, userId);
                var globalGeneration = Interlocked.Read(ref _globalGeneration);
                var roleId = await roleResolver(cancellationToken);

                if (userGeneration != GetGeneration(_userGenerations, userId)
                    || globalGeneration != Interlocked.Read(ref _globalGeneration))
                {
                    continue;
                }

                var roleGeneration = roleId.HasValue
                    ? GetGeneration(_roleGenerations, roleId.Value)
                    : 0;
                var loadStarted = Stopwatch.GetTimestamp();
                PermissionCacheSnapshot snapshot;
                try
                {
                    snapshot = await loader(cancellationToken);
                    CacheLoads.Add(1);
                }
                catch
                {
                    CacheLoadFailures.Add(1);
                    throw;
                }
                finally
                {
                    CacheLoadDuration.Record(Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds);
                }

                if (snapshot.RoleId != roleId)
                {
                    continue;
                }

                if (userGeneration != GetGeneration(_userGenerations, userId)
                    || globalGeneration != Interlocked.Read(ref _globalGeneration)
                    || (roleId.HasValue
                        && roleGeneration != GetGeneration(_roleGenerations, roleId.Value)))
                {
                    continue;
                }

                var immutableCodes = snapshot.PermissionCodes
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var entry = new CacheEntry(
                    userGeneration,
                    globalGeneration,
                    roleId,
                    roleGeneration,
                    immutableCodes);

                _memoryCache.Set(
                    GetCacheKey(userId, userGeneration),
                    entry,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _expiration
                    });

                if (IsCurrent(userId, entry))
                {
                    return CreatePermissionSet(entry.PermissionCodes);
                }
            }
        }
    }

    public void InvalidateUser(Guid userId)
    {
        _userGenerations.AddOrUpdate(userId, 1, static (_, generation) => checked(generation + 1));
        CacheInvalidations.Add(1, new KeyValuePair<string, object?>("scope", "user"));
    }

    public void InvalidateRole(Guid roleId)
    {
        _roleGenerations.AddOrUpdate(roleId, 1, static (_, generation) => checked(generation + 1));
        CacheInvalidations.Add(1, new KeyValuePair<string, object?>("scope", "role"));
    }

    public void InvalidateAll()
    {
        Interlocked.Increment(ref _globalGeneration);
        CacheInvalidations.Add(1, new KeyValuePair<string, object?>("scope", "all"));
    }

    private bool TryGetCurrent(Guid userId, out IReadOnlySet<string> permissions)
    {
        var userGeneration = GetGeneration(_userGenerations, userId);
        if (_memoryCache.TryGetValue(GetCacheKey(userId, userGeneration), out CacheEntry? entry)
            && entry is not null
            && IsCurrent(userId, entry))
        {
            CacheHits.Add(1);
            permissions = CreatePermissionSet(entry.PermissionCodes);
            return true;
        }

        CacheMisses.Add(1);
        permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    private bool IsCurrent(Guid userId, CacheEntry entry)
    {
        if (entry.UserGeneration != GetGeneration(_userGenerations, userId)
            || entry.GlobalGeneration != Interlocked.Read(ref _globalGeneration))
        {
            return false;
        }

        return !entry.RoleId.HasValue
            || entry.RoleGeneration == GetGeneration(_roleGenerations, entry.RoleId.Value);
    }

    private static long GetGeneration(ConcurrentDictionary<Guid, long> generations, Guid id)
    {
        return generations.GetOrAdd(id, 0);
    }

    private static string GetCacheKey(Guid userId, long generation)
    {
        return $"{CacheKeyPrefix}:{userId}:generation:{generation}";
    }

    private static IReadOnlySet<string> CreatePermissionSet(IEnumerable<string> permissionCodes)
    {
        return new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record CacheEntry(
        long UserGeneration,
        long GlobalGeneration,
        Guid? RoleId,
        long RoleGeneration,
        string[] PermissionCodes);
}

public sealed class PermissionCacheOptions
{
    public int ExpirationSeconds { get; set; } = 60;
}

internal sealed class KeyedAsyncLock<TKey> where TKey : notnull
{
    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries = new();

    public async ValueTask<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void Release(TKey key, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(TKey key, Entry entry)
    {
        lock (_sync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock<TKey> _owner;
        private readonly TKey _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(KeyedAsyncLock<TKey> owner, TKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }
        }
    }
}
