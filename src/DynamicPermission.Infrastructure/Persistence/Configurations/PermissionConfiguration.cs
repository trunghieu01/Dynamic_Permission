using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DynamicPermission.Infrastructure.Persistence.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        // Primary Key
        builder.HasKey(p => p.Id);

        // Properties
        builder.Property(p => p.Code)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(p => p.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Module)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(p => p.Action)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasMaxLength(255);

        // Audit fields
        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        builder.Property(p => p.CreatedBy)
            .HasMaxLength(100);

        builder.Property(p => p.UpdatedAt);

        builder.Property(p => p.UpdatedBy)
            .HasMaxLength(100);

        // Indexes & Constraints
        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("IX_Permissions_Code");

        builder.HasIndex(p => new { p.Module, p.Action })
            .HasDatabaseName("IX_Permissions_Module_Action");

        // Relationships
        builder.HasMany(p => p.RolePermissions)
            .WithOne(rp => rp.Permission)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.UserPermissions)
            .WithOne(up => up.Permission)
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
