using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.UnitTests.Common;

public static class TestDbContextFactory
{
    public static DynamicPermissionDbContext Create(string? dbName = null, IPermissionCache? permissionCache = null)
    {
        var options = new DbContextOptionsBuilder<DynamicPermissionDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new DynamicPermissionDbContext(options, permissionCache);
        context.Database.EnsureCreated();
        return context;
    }
}
