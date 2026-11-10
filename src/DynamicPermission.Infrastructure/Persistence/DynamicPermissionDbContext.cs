using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Infrastructure.Authorization;
using DynamicPermission.Domain.Common;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Infrastructure.Persistence;

public class DynamicPermissionDbContext : DbContext, IApplicationDbContext
{
    private readonly IPermissionCache? _permissionCache;
    private readonly PermissionCacheTransactionInterceptor? _transactionInterceptor;

    public DynamicPermissionDbContext(
        DbContextOptions<DynamicPermissionDbContext> options,
        IPermissionCache? permissionCache = null,
        PermissionCacheTransactionInterceptor? transactionInterceptor = null) : base(options)
    {
        _permissionCache = permissionCache;
        _transactionInterceptor = transactionInterceptor;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DynamicPermissionDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        var permissionChanges = GetPermissionChanges();
        return SaveChangesAndInvalidatePermissionsAsync(permissionChanges, cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        var permissionChanges = GetPermissionChanges();
        var result = base.SaveChanges();
        InvalidatePermissions(permissionChanges);
        return result;
    }

    private async Task<int> SaveChangesAndInvalidatePermissionsAsync(
        PermissionCacheInvalidation changes,
        CancellationToken cancellationToken)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        InvalidatePermissions(changes);
        return result;
    }

    private PermissionCacheInvalidation GetPermissionChanges()
    {
        var changes = new PermissionCacheInvalidation();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case User user:
                    changes.UserIds.Add(user.Id);
                    break;
                case UserPermission userPermission:
                    changes.UserIds.Add(userPermission.UserId);
                    break;
                case Role role:
                    changes.RoleIds.Add(role.Id);
                    break;
                case RolePermission rolePermission:
                    changes.RoleIds.Add(rolePermission.RoleId);
                    break;
                case Permission:
                    changes.InvalidateAll = true;
                    break;
            }
        }

        return changes;
    }

    private void InvalidatePermissions(PermissionCacheInvalidation changes)
    {
        if (_transactionInterceptor is not null)
        {
            _transactionInterceptor.InvalidateAfterSave(this, changes);
            return;
        }

        if (_permissionCache is not null)
        {
            changes.Apply(_permissionCache);
        }
    }

    private void UpdateAuditFields()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity<Guid> auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    if (auditable.CreatedAt == default)
                        auditable.CreatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                }
            }
            else if (entry.Entity is AuditableJoinEntity joinEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    if (joinEntity.CreatedAt == default)
                        joinEntity.CreatedAt = now;
                }
            }
            else if (entry.Entity is UserPermission userPermission)
            {
                if (entry.State == EntityState.Added)
                {
                    if (userPermission.CreatedAt == default)
                        userPermission.CreatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    userPermission.UpdatedAt = now;
                }
            }
        }
    }
}
