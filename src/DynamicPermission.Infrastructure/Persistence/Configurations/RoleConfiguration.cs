using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DynamicPermission.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        // Primary Key
        builder.HasKey(r => r.Id);

        // Properties
        builder.Property(r => r.Code)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(r => r.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasMaxLength(255);

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Audit fields
        builder.Property(r => r.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        builder.Property(r => r.CreatedBy)
            .HasMaxLength(100);

        builder.Property(r => r.UpdatedAt);

        builder.Property(r => r.UpdatedBy)
            .HasMaxLength(100);

        // Indexes
        builder.HasIndex(r => r.Code)
            .IsUnique()
            .HasDatabaseName("IX_Roles_Code");

        builder.HasIndex(r => r.IsActive)
            .HasDatabaseName("IX_Roles_IsActive");

        // Relationships
        // 1 Role has many Users (1 - N)
        // Restrict delete: Role cannot be deleted if referenced by any User
        builder.HasMany(r => r.Users)
            .WithOne(u => u.Role)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1 Role has many RolePermissions (N - N)
        builder.HasMany(r => r.RolePermissions)
            .WithOne(rp => rp.Role)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
