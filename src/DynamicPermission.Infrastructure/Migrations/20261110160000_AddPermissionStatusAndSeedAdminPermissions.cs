using DynamicPermission.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DynamicPermission.Infrastructure.Migrations;

[DbContext(typeof(DynamicPermissionDbContext))]
[Migration("20261110160000_AddPermissionStatusAndSeedAdminPermissions")]
public partial class AddPermissionStatusAndSeedAdminPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Permissions",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Code] = 'ADMIN')
            BEGIN
                INSERT INTO [Roles] ([Id], [Code], [Name], [Description], [IsActive], [CreatedAt])
                VALUES (NEWID(), 'ADMIN', N'Administrator', N'Seeded administration role', 1, SYSDATETIMEOFFSET());
            END;

            DECLARE @PermissionSeeds TABLE
            (
                [Code] varchar(100) NOT NULL,
                [Name] nvarchar(100) NOT NULL,
                [Module] varchar(50) NOT NULL,
                [Action] varchar(50) NOT NULL
            );

            INSERT INTO @PermissionSeeds ([Code], [Name], [Module], [Action])
            VALUES
                ('user.view', N'View users', 'user', 'view'),
                ('user.create', N'Create users', 'user', 'create'),
                ('user.update', N'Update users', 'user', 'update'),
                ('user.lock', N'Lock or unlock users', 'user', 'lock'),
                ('user.role.assign', N'Assign user roles', 'user', 'role.assign'),
                ('user.permissions.view', N'View effective user permissions', 'user', 'permissions.view'),
                ('role.view', N'View roles', 'role', 'view'),
                ('role.create', N'Create roles', 'role', 'create'),
                ('role.update', N'Update roles', 'role', 'update'),
                ('role.deactivate', N'Deactivate roles', 'role', 'deactivate'),
                ('role.permission.assign', N'Assign role permissions', 'role', 'permission.assign'),
                ('permission.view', N'View permissions', 'permission', 'view'),
                ('permission.create', N'Create permissions', 'permission', 'create'),
                ('permission.update', N'Update permissions', 'permission', 'update'),
                ('permission.deactivate', N'Deactivate permissions', 'permission', 'deactivate');

            INSERT INTO [Permissions] ([Id], [Code], [Name], [Module], [Action], [IsActive], [CreatedAt])
            SELECT NEWID(), seed.[Code], seed.[Name], seed.[Module], seed.[Action], 1, SYSDATETIMEOFFSET()
            FROM @PermissionSeeds AS seed
            WHERE NOT EXISTS (SELECT 1 FROM [Permissions] AS existing WHERE existing.[Code] = seed.[Code]);

            DECLARE @AdminRoleId uniqueidentifier;
            SELECT @AdminRoleId = [Id] FROM [Roles] WHERE [Code] = 'ADMIN';

            INSERT INTO [RolePermissions] ([RoleId], [PermissionId], [CreatedAt])
            SELECT @AdminRoleId, permission.[Id], SYSDATETIMEOFFSET()
            FROM [Permissions] AS permission
            INNER JOIN @PermissionSeeds AS seed ON seed.[Code] = permission.[Code]
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [RolePermissions] AS existing
                WHERE existing.[RoleId] = @AdminRoleId
                  AND existing.[PermissionId] = permission.[Id]
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Permissions");
    }
}
