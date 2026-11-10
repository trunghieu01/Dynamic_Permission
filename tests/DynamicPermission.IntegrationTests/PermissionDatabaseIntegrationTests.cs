using System.Data.Common;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Services;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Authorization;
using DynamicPermission.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.Sqlite;

namespace DynamicPermission.IntegrationTests;

public sealed class PermissionDatabaseIntegrationTests
{
    [Fact]
    public async Task TransactionCommit_ShouldApplyPermissionCacheInvalidationAfterCommit()
    {
        await using var fixture = await SqlitePermissionFixture.CreateAsync();
        var (userId, roleId, permissionId) = await fixture.SeedAsync("transaction.commit");
        var permissionService = fixture.CreatePermissionService();

        (await permissionService.GetUserEffectivePermissionsAsync(userId)).Should().Contain("transaction.commit");

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.RolePermissions.Remove(await fixture.Context.RolePermissions.SingleAsync(
                item => item.RoleId == roleId && item.PermissionId == permissionId));
            await fixture.Context.SaveChangesAsync();

            (await permissionService.GetUserEffectivePermissionsAsync(userId))
                .Should().Contain("transaction.commit", "invalidation is deferred until commit");

            await transaction.CommitAsync();
        }

        (await permissionService.GetUserEffectivePermissionsAsync(userId))
            .Should().NotContain("transaction.commit");
    }

    [Fact]
    public async Task TransactionRollback_ShouldDiscardPendingPermissionCacheInvalidation()
    {
        await using var fixture = await SqlitePermissionFixture.CreateAsync();
        var (userId, roleId, permissionId) = await fixture.SeedAsync("transaction.rollback");
        var permissionService = fixture.CreatePermissionService();
        (await permissionService.GetUserEffectivePermissionsAsync(userId)).Should().Contain("transaction.rollback");

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.RolePermissions.Remove(await fixture.Context.RolePermissions.SingleAsync(
                item => item.RoleId == roleId && item.PermissionId == permissionId));
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        (await permissionService.GetUserEffectivePermissionsAsync(userId))
            .Should().Contain("transaction.rollback");
        (await fixture.Context.RolePermissions.CountAsync(
            item => item.RoleId == roleId && item.PermissionId == permissionId)).Should().Be(1);
    }

    [Fact]
    public async Task PermissionCacheHit_ShouldAvoidAdditionalDatabaseQueries()
    {
        await using var fixture = await SqlitePermissionFixture.CreateAsync();
        var (userId, _, _) = await fixture.SeedAsync("performance.permission");
        var permissionService = fixture.CreatePermissionService();
        fixture.CommandCounter.Reset();

        (await permissionService.GetUserEffectivePermissionsAsync(userId))
            .Should().Contain("performance.permission");
        var queryCountAfterMiss = fixture.CommandCounter.Count;

        (await permissionService.GetUserEffectivePermissionsAsync(userId))
            .Should().Contain("performance.permission");

        queryCountAfterMiss.Should().Be(2, "one query resolves the role and one loads the snapshot");
        fixture.CommandCounter.Count.Should().Be(queryCountAfterMiss, "cache hits must not query the database");
    }

    [Fact]
    public async Task UserRoleRelationship_ShouldRequireOneExistingRole()
    {
        await using var fixture = await SqlitePermissionFixture.CreateAsync();
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = Guid.NewGuid(),
            Username = "roleless",
            Email = "roleless@example.test",
            PasswordHash = "not-a-real-password-hash",
            FullName = "Roleless"
        };
        var roleForeignKey = fixture.Context.Model.FindEntityType(typeof(User))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(User.RoleId)));
        roleForeignKey.IsRequired.Should().BeTrue();

        fixture.Context.Users.Add(user);
        var save = () => fixture.Context.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    private sealed class SqlitePermissionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MemoryCache _memoryCache;

        private SqlitePermissionFixture(
            SqliteConnection connection,
            MemoryCache memoryCache,
            PermissionCacheTransactionInterceptor transactionInterceptor,
            MemoryPermissionCache permissionCache,
            DbContextOptions<DynamicPermissionDbContext> options,
            DbCommandCounter commandCounter)
        {
            _connection = connection;
            _memoryCache = memoryCache;
            PermissionCache = permissionCache;
            Context = new DynamicPermissionDbContext(options, permissionCache, transactionInterceptor);
            CommandCounter = commandCounter;
        }

        public DynamicPermissionDbContext Context { get; }
        public IPermissionCache PermissionCache { get; }
        public DbCommandCounter CommandCounter { get; }

        public static async Task<SqlitePermissionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var permissionCache = new MemoryPermissionCache(memoryCache, TimeSpan.FromMinutes(1));
            var transactionInterceptor = new PermissionCacheTransactionInterceptor(permissionCache);
            var commandCounter = new DbCommandCounter();
            var options = new DbContextOptionsBuilder<DynamicPermissionDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(transactionInterceptor, commandCounter)
                .Options;
            var fixture = new SqlitePermissionFixture(
                connection,
                memoryCache,
                transactionInterceptor,
                permissionCache,
                options,
                commandCounter);
            await fixture.Context.Database.EnsureCreatedAsync();
            return fixture;
        }

        public PermissionService CreatePermissionService() => new(Context, PermissionCache);

        public async Task<(Guid UserId, Guid RoleId, Guid PermissionId)> SeedAsync(string permissionCode)
        {
            var role = PermissionApiHost.CreateRole("SQLITE_" + Guid.NewGuid().ToString("N"));
            var user = new User
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                Username = "user_" + Guid.NewGuid().ToString("N"),
                Email = Guid.NewGuid().ToString("N") + "@example.test",
                PasswordHash = "not-a-real-password-hash",
                FullName = "Integration User"
            };
            var permission = PermissionApiHost.CreatePermission(permissionCode);
            Context.Roles.Add(role);
            Context.Users.Add(user);
            Context.Permissions.Add(permission);
            Context.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id
            });
            await Context.SaveChangesAsync();
            return (user.Id, role.Id, permission.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            _memoryCache.Dispose();
            await _connection.DisposeAsync();
        }
    }

    public sealed class DbCommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuted(command, eventData, result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
