using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Domain.Common;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Infrastructure.Persistence;

public class DynamicPermissionDbContext : DbContext, IApplicationDbContext
{
    private readonly IPermissionCache? _permissionCache;

    public DynamicPermissionDbContext(
        DbContextOptions<DynamicPermissionDbContext> options,
        IPermissionCache? permissionCache = null) : base(options)
    {
        _permissionCache = permissionCache;
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
        var shouldInvalidatePermissions = HasPermissionChanges();
        return SaveChangesAndInvalidatePermissionsAsync(shouldInvalidatePermissions, cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        var shouldInvalidatePermissions = HasPermissionChanges();
        var result = base.SaveChanges();
        InvalidatePermissions(shouldInvalidatePermissions);
        return result;
    }

    private async Task<int> SaveChangesAndInvalidatePermissionsAsync(
        bool shouldInvalidatePermissions,
        CancellationToken cancellationToken)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        InvalidatePermissions(shouldInvalidatePermissions);
        return result;
    }

    private bool HasPermissionChanges()
    {
        return ChangeTracker.Entries().Any(entry =>
            (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && (entry.Entity is User or Role or Permission or RolePermission or UserPermission));
    }

    private void InvalidatePermissions(bool shouldInvalidatePermissions)
    {
        if (shouldInvalidatePermissions)
        {
            _permissionCache?.InvalidateAll();
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
