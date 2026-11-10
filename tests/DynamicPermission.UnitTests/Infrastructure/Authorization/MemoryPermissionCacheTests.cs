using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace DynamicPermission.UnitTests.Infrastructure.Authorization;

public class MemoryPermissionCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_ShouldLoadOnce_AndReturnCachedPermissionSet()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var loadCount = 0;

        async Task<PermissionCacheSnapshot> Load(CancellationToken _)
        {
            loadCount++;
            await Task.Yield();
            return Snapshot(roleId, "user.view");
        }

        var first = await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(roleId), Load, default);
        var second = await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(roleId), Load, default);

        loadCount.Should().Be(1);
        first.Should().Contain("user.view");
        second.Should().Contain("user.view");
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCacheEmptyPermissionSet()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var loadCount = 0;

        Task<PermissionCacheSnapshot> Load(CancellationToken _)
        {
            loadCount++;
            return Task.FromResult(Snapshot(null));
        }

        (await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(null), Load, default)).Should().BeEmpty();
        (await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(null), Load, default)).Should().BeEmpty();

        loadCount.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateUser_ShouldInvalidateOnlyThatUser()
    {
        var cache = CreateCache();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var firstLoads = 0;
        var secondLoads = 0;

        Task<PermissionCacheSnapshot> LoadFirst(CancellationToken _)
        {
            firstLoads++;
            return Task.FromResult(Snapshot(roleId, "user.view"));
        }

        Task<PermissionCacheSnapshot> LoadSecond(CancellationToken _)
        {
            secondLoads++;
            return Task.FromResult(Snapshot(roleId, "user.view"));
        }

        await cache.GetOrCreateAsync(firstUserId, _ => Task.FromResult<Guid?>(roleId), LoadFirst, default);
        await cache.GetOrCreateAsync(secondUserId, _ => Task.FromResult<Guid?>(roleId), LoadSecond, default);
        cache.InvalidateUser(firstUserId);

        await cache.GetOrCreateAsync(firstUserId, _ => Task.FromResult<Guid?>(roleId), LoadFirst, default);
        await cache.GetOrCreateAsync(secondUserId, _ => Task.FromResult<Guid?>(roleId), LoadSecond, default);

        firstLoads.Should().Be(2);
        secondLoads.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateRole_ShouldInvalidateUsersWithThatRoleOnly()
    {
        var cache = CreateCache();
        var sharedRoleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();
        var sharedUserOne = Guid.NewGuid();
        var sharedUserTwo = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var loads = new Dictionary<Guid, int>();

        Task<PermissionCacheSnapshot> Loader(Guid userId, Guid roleId)
        {
            loads[userId] = loads.GetValueOrDefault(userId) + 1;
            return Task.FromResult(Snapshot(roleId, "role.permission"));
        }

        await LoadAsync(cache, sharedUserOne, sharedRoleId, Loader);
        await LoadAsync(cache, sharedUserTwo, sharedRoleId, Loader);
        await LoadAsync(cache, otherUser, otherRoleId, Loader);
        cache.InvalidateRole(sharedRoleId);

        await LoadAsync(cache, sharedUserOne, sharedRoleId, Loader);
        await LoadAsync(cache, sharedUserTwo, sharedRoleId, Loader);
        await LoadAsync(cache, otherUser, otherRoleId, Loader);

        loads[sharedUserOne].Should().Be(2);
        loads[sharedUserTwo].Should().Be(2);
        loads[otherUser].Should().Be(1);
    }

    [Fact]
    public async Task InvalidateAll_ShouldInvalidateEveryUser()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var loads = 0;

        Task<PermissionCacheSnapshot> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(Snapshot(roleId, "user.view"));
        }

        await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(roleId), Load, default);
        cache.InvalidateAll();
        await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(roleId), Load, default);

        loads.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCoalesceConcurrentMissesForSameUser()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var loadCount = 0;
        var resolverCount = 0;
        var startLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Guid?> ResolveRole(CancellationToken _)
        {
            Interlocked.Increment(ref resolverCount);
            return await Task.FromResult<Guid?>(roleId);
        }

        async Task<PermissionCacheSnapshot> Load(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref loadCount);
            startLoad.TrySetResult();
            await Task.Delay(50, cancellationToken);
            return Snapshot(roleId, "user.view");
        }

        var requests = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => cache.GetOrCreateAsync(userId, ResolveRole, Load, default)))
            .ToArray();
        var results = await Task.WhenAll(requests);

        await startLoad.Task;
        loadCount.Should().Be(1);
        resolverCount.Should().Be(1);
        results.Should().OnlyContain(result => result.Contains("user.view"));
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldLoadDifferentUsersConcurrently()
    {
        var cache = CreateCache();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var firstRoleId = Guid.NewGuid();
        var secondRoleId = Guid.NewGuid();
        var started = 0;
        var bothLoadsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PermissionCacheSnapshot> Load(Guid roleId)
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothLoadsStarted.TrySetResult();
            }

            await releaseLoads.Task;
            return Snapshot(roleId, "user.view");
        }

        var first = cache.GetOrCreateAsync(
            firstUserId,
            _ => Task.FromResult<Guid?>(firstRoleId),
            _ => Load(firstRoleId),
            default);
        var second = cache.GetOrCreateAsync(
            secondUserId,
            _ => Task.FromResult<Guid?>(secondRoleId),
            _ => Load(secondRoleId),
            default);

        await bothLoadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseLoads.TrySetResult();
        await Task.WhenAll(first, second);

        started.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldNotCacheInFlightSnapshotAfterRoleInvalidation()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstLoadToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;

        async Task<PermissionCacheSnapshot> Load(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref loadCount);
            if (call == 1)
            {
                loadStarted.TrySetResult();
                await allowFirstLoadToFinish.Task.WaitAsync(cancellationToken);
                return Snapshot(roleId, "permission.before");
            }

            return Snapshot(roleId, "permission.after");
        }

        var request = cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            Load,
            default);

        await loadStarted.Task;
        cache.InvalidateRole(roleId);
        allowFirstLoadToFinish.TrySetResult();

        var permissions = await request;

        permissions.Should().Contain("permission.after");
        permissions.Should().NotContain("permission.before");
        loadCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldReleaseSingleFlightLock_WhenLoaderThrows()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var calls = 0;

        Task<PermissionCacheSnapshot> Load(CancellationToken _)
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("Database unavailable.");
            }

            return Task.FromResult(Snapshot(roleId, "user.view"));
        }

        var failedRequest = () => cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            Load,
            default);

        await failedRequest.Should().ThrowAsync<InvalidOperationException>();
        var permissions = await cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            Load,
            default);

        permissions.Should().Contain("user.view");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldReleaseWaiter_WhenItsRequestIsCancelled()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PermissionCacheSnapshot> Load(CancellationToken cancellationToken)
        {
            loadStarted.TrySetResult();
            await releaseLoad.Task.WaitAsync(cancellationToken);
            return Snapshot(roleId, "user.view");
        }

        var leader = cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            Load,
            default);
        await loadStarted.Task;

        using var cancellation = new CancellationTokenSource();
        var waiter = cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            Load,
            cancellation.Token);
        cancellation.Cancel();

        var cancelAction = async () => await waiter;
        await cancelAction.Should().ThrowAsync<OperationCanceledException>();

        releaseLoad.TrySetResult();
        (await leader).Should().Contain("user.view");
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldExpireCacheEntry()
    {
        var cache = CreateCache(TimeSpan.FromMilliseconds(50));
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var loadCount = 0;

        Task<PermissionCacheSnapshot> Load(CancellationToken _)
        {
            loadCount++;
            return Task.FromResult(Snapshot(roleId, "user.view"));
        }

        await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(roleId), Load, default);
        await Task.Delay(120);
        await cache.GetOrCreateAsync(userId, _ => Task.FromResult<Guid?>(roleId), Load, default);

        loadCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldReturnCopiesThatCannotMutateCachedPermissions()
    {
        var cache = CreateCache();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var first = await cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            _ => Task.FromResult(Snapshot(roleId, "user.view")),
            default);

        ((HashSet<string>)first).Add("admin.delete");

        var second = await cache.GetOrCreateAsync(
            userId,
            _ => throw new InvalidOperationException("Cache hit must not resolve the role."),
            _ => throw new InvalidOperationException("Cache hit must not load permissions."),
            default);

        second.Should().Contain("user.view");
        second.Should().NotContain("admin.delete");
    }

    private static MemoryPermissionCache CreateCache(TimeSpan? expiration = null)
    {
        return new MemoryPermissionCache(
            new MemoryCache(new MemoryCacheOptions()),
            expiration ?? TimeSpan.FromMinutes(1));
    }

    private static PermissionCacheSnapshot Snapshot(Guid? roleId, params string[] permissions)
    {
        return new PermissionCacheSnapshot(roleId, permissions);
    }

    private static Task LoadAsync(
        IPermissionCache cache,
        Guid userId,
        Guid roleId,
        Func<Guid, Guid, Task<PermissionCacheSnapshot>> loader)
    {
        return cache.GetOrCreateAsync(
            userId,
            _ => Task.FromResult<Guid?>(roleId),
            _ => loader(userId, roleId),
            default);
    }
}
