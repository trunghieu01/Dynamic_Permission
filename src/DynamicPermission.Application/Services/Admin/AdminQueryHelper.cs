using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;
using DynamicPermission.Application.Models.Admin.Users;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Application.Services.Admin;

internal static class AdminQueryHelper
{
    public static async Task<PagedResponse<T>> ToPagedResponseAsync<TEntity, T>(
        IQueryable<TEntity> query,
        PageQuery page,
        Func<TEntity, T> map,
        CancellationToken cancellationToken)
    {
        ValidatePage(page);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<T>(
            items.Select(map).ToArray(),
            page.Page,
            page.PageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)page.PageSize));
    }

    public static void ValidatePage(PageQuery page)
    {
        if (page.Page < 1 || page.PageSize is < 1 or > 100)
        {
            throw new ArgumentException("Page must be positive and PageSize must be between 1 and 100.");
        }

        if (page.Page > int.MaxValue / page.PageSize)
        {
            throw new ArgumentException("The requested page is too large.");
        }

        if (!string.Equals(page.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(page.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("SortDirection must be 'asc' or 'desc'.");
        }
    }

    public static IQueryable<User> ApplyUserSort(IQueryable<User> query, UserListQuery options)
    {
        var descending = string.Equals(options.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return options.SortBy?.ToLowerInvariant() switch
        {
            null or "" => query.OrderBy(user => user.Username).ThenBy(user => user.Id),
            "username" => descending ? query.OrderByDescending(user => user.Username).ThenBy(user => user.Id) : query.OrderBy(user => user.Username).ThenBy(user => user.Id),
            "email" => descending ? query.OrderByDescending(user => user.Email).ThenBy(user => user.Id) : query.OrderBy(user => user.Email).ThenBy(user => user.Id),
            "fullname" => descending ? query.OrderByDescending(user => user.FullName).ThenBy(user => user.Id) : query.OrderBy(user => user.FullName).ThenBy(user => user.Id),
            "isactive" => descending ? query.OrderByDescending(user => user.IsActive).ThenBy(user => user.Id) : query.OrderBy(user => user.IsActive).ThenBy(user => user.Id),
            "createdat" => descending ? query.OrderByDescending(user => user.CreatedAt).ThenBy(user => user.Id) : query.OrderBy(user => user.CreatedAt).ThenBy(user => user.Id),
            _ => throw new ArgumentException("Unsupported user sort field.")
        };
    }

    public static IQueryable<Role> ApplyRoleSort(IQueryable<Role> query, RoleListQuery options)
    {
        var descending = string.Equals(options.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return options.SortBy?.ToLowerInvariant() switch
        {
            null or "" => query.OrderBy(role => role.Code).ThenBy(role => role.Id),
            "code" => descending ? query.OrderByDescending(role => role.Code).ThenBy(role => role.Id) : query.OrderBy(role => role.Code).ThenBy(role => role.Id),
            "name" => descending ? query.OrderByDescending(role => role.Name).ThenBy(role => role.Id) : query.OrderBy(role => role.Name).ThenBy(role => role.Id),
            "isactive" => descending ? query.OrderByDescending(role => role.IsActive).ThenBy(role => role.Id) : query.OrderBy(role => role.IsActive).ThenBy(role => role.Id),
            "createdat" => descending ? query.OrderByDescending(role => role.CreatedAt).ThenBy(role => role.Id) : query.OrderBy(role => role.CreatedAt).ThenBy(role => role.Id),
            _ => throw new ArgumentException("Unsupported role sort field.")
        };
    }

    public static IQueryable<Permission> FilterPermissions(
        IQueryable<Permission> query,
        PermissionListQuery options)
    {
        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var search = options.Search.Trim();
            query = query.Where(permission => permission.Code.Contains(search) || permission.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(options.Module))
        {
            var module = options.Module.Trim();
            query = query.Where(permission => permission.Module == module);
        }

        if (!string.IsNullOrWhiteSpace(options.Action))
        {
            var action = options.Action.Trim();
            query = query.Where(permission => permission.Action == action);
        }

        if (options.IsActive.HasValue)
        {
            query = query.Where(permission => permission.IsActive == options.IsActive.Value);
        }

        return query;
    }

    public static IQueryable<Permission> ApplyPermissionSort(
        IQueryable<Permission> query,
        PermissionListQuery options)
    {
        var descending = string.Equals(options.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return options.SortBy?.ToLowerInvariant() switch
        {
            null or "" => query.OrderBy(permission => permission.Code).ThenBy(permission => permission.Id),
            "code" => descending ? query.OrderByDescending(permission => permission.Code).ThenBy(permission => permission.Id) : query.OrderBy(permission => permission.Code).ThenBy(permission => permission.Id),
            "name" => descending ? query.OrderByDescending(permission => permission.Name).ThenBy(permission => permission.Id) : query.OrderBy(permission => permission.Name).ThenBy(permission => permission.Id),
            "module" => descending ? query.OrderByDescending(permission => permission.Module).ThenBy(permission => permission.Id) : query.OrderBy(permission => permission.Module).ThenBy(permission => permission.Id),
            "action" => descending ? query.OrderByDescending(permission => permission.Action).ThenBy(permission => permission.Id) : query.OrderBy(permission => permission.Action).ThenBy(permission => permission.Id),
            "isactive" => descending ? query.OrderByDescending(permission => permission.IsActive).ThenBy(permission => permission.Id) : query.OrderBy(permission => permission.IsActive).ThenBy(permission => permission.Id),
            "createdat" => descending ? query.OrderByDescending(permission => permission.CreatedAt).ThenBy(permission => permission.Id) : query.OrderBy(permission => permission.CreatedAt).ThenBy(permission => permission.Id),
            _ => throw new ArgumentException("Unsupported permission sort field.")
        };
    }
}
