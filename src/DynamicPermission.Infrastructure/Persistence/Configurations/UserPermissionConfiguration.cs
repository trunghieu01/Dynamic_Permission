using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DynamicPermission.Infrastructure.Persistence.Configurations;

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("UserPermissions");

        // Composite Primary Key (No duplicate overrides)
        builder.HasKey(up => new { up.UserId, up.PermissionId });

        builder.Property(up => up.IsGranted)
            .IsRequired();

        // Audit fields
        builder.Property(up => up.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        builder.Property(up => up.CreatedBy)
            .HasMaxLength(100);

        builder.Property(up => up.UpdatedAt);

        builder.Property(up => up.UpdatedBy)
            .HasMaxLength(100);

        // Secondary Index on PermissionId
        builder.HasIndex(up => up.PermissionId)
            .HasDatabaseName("IX_UserPermissions_PermissionId");

        // Foreign Key Relationships
        builder.HasOne(up => up.User)
            .WithMany(u => u.UserPermissions)
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(up => up.Permission)
            .WithMany(p => p.UserPermissions)
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
