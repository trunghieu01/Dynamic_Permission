using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Services.Admin;
using DynamicPermission.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicPermission.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleManagementService, RoleManagementService>();
        services.AddScoped<IPermissionManagementService, PermissionManagementService>();
        return services;
    }
}
