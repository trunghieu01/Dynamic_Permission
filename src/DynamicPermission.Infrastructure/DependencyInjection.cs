using System.Text;
using System.Text.Json;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Infrastructure.Authorization;
using DynamicPermission.Infrastructure.Authentication;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DynamicPermission.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<IPermissionCache, MemoryPermissionCache>();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // 1. Database Context
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in configuration.");

        services.AddDbContext<DynamicPermissionDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(DynamicPermissionDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
        },
        contextLifetime: ServiceLifetime.Scoped,
        optionsLifetime: ServiceLifetime.Scoped);

        // Map IApplicationDbContext to Scoped DbContext
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<DynamicPermissionDbContext>());

        // 2. HTTP Context Accessor & Current User
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // 3. Password Hasher
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();

        // 4. JWT Settings & Token Service
        var jwtSettingsSection = configuration.GetSection(JwtSettings.SectionName);
        services.Configure<JwtSettings>(jwtSettingsSection);
        var jwtSettings = jwtSettingsSection.Get<JwtSettings>() ?? new JwtSettings();

        services.AddSingleton<ITokenService, TokenService>();

        // 5. JWT Bearer Authentication & Unauthorized handling
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            // Custom handling for 401 Unauthorized and 403 Forbidden responses
            options.Events = new JwtBearerEvents
            {
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    var result = JsonSerializer.Serialize(new
                    {
                        status = 401,
                        title = "Unauthorized",
                        detail = "Bạn chưa đăng nhập hoặc phiên làm việc đã hết hạn. Vui lòng cung cấp Access Token hợp lệ.",
                        timestamp = DateTimeOffset.UtcNow
                    });
                    return context.Response.WriteAsync(result);
                },
                OnForbidden = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    var result = JsonSerializer.Serialize(new
                    {
                        status = 403,
                        title = "Forbidden",
                        detail = "Bạn không có quyền truy cập vào tài nguyên này.",
                        timestamp = DateTimeOffset.UtcNow
                    });
                    return context.Response.WriteAsync(result);
                }
            };
        });

        return services;
    }
}
