using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using DynamicPermission.API.Controllers;
using DynamicPermission.API.Middleware;
using DynamicPermission.Application;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Infrastructure.Authentication;
using DynamicPermission.Infrastructure.Authorization;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace DynamicPermission.IntegrationTests;

internal sealed class PermissionApiHost : IAsyncDisposable
{
    public const string TestSecret = "IntegrationTestSecretKey_Only_For_Testing_12345";
    public const string TestIssuer = "DynamicPermission.IntegrationTests";
    public const string TestAudience = "DynamicPermission.IntegrationTests.Client";

    private readonly TestServer _server;
    private readonly HttpClient _client;

    private PermissionApiHost(TestServer server)
    {
        _server = server;
        _client = server.CreateClient();
    }

    public HttpClient Client => _client;
    public IServiceProvider Services => _server.Services;

    public static async Task<PermissionApiHost> StartAsync(int cacheExpirationSeconds = 60)
    {
        var databaseName = $"permission-api-{Guid.NewGuid():N}";
        var values = new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = TestSecret,
            ["JwtSettings:Issuer"] = TestIssuer,
            ["JwtSettings:Audience"] = TestAudience,
            ["JwtSettings:AccessTokenExpirationMinutes"] = "30",
            ["JwtSettings:RefreshTokenExpirationDays"] = "7",
            ["PermissionCache:ExpirationSeconds"] = cacheExpirationSeconds.ToString(),
            ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=unused;Trusted_Connection=True"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var server = new TestServer(new WebHostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddConfiguration(configuration))
            .ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Error))
            .ConfigureServices(services =>
            {
                services.AddApplicationServices();
                services.AddMemoryCache();
                services.Configure<PermissionCacheOptions>(configuration.GetSection("PermissionCache"));
                services.AddSingleton<IPermissionCache, MemoryPermissionCache>();
                services.AddScoped<PermissionCacheTransactionInterceptor>();
                services.AddAuthorization();
                services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
                    PermissionAuthorizationPolicyProvider>();
                services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                    PermissionAuthorizationHandler>();

                services.AddDbContext<DynamicPermissionDbContext>((provider, options) =>
                {
                    options.AddInterceptors(provider.GetRequiredService<PermissionCacheTransactionInterceptor>());
                    options.UseInMemoryDatabase(databaseName);
                });
                services.AddScoped<IApplicationDbContext>(provider =>
                    provider.GetRequiredService<DynamicPermissionDbContext>());
                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUserService, CurrentUserService>();
                services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
                services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
                services.AddSingleton<ITokenService, TokenService>();
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = TestIssuer,
                            ValidateAudience = true,
                            ValidAudience = TestAudience,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero
                        };
                    });
                services.AddControllers()
                    .AddApplicationPart(typeof(DemoController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseMiddleware<ApiExceptionMiddleware>();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        await using var scope = server.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<DynamicPermissionDbContext>()
            .Database
            .EnsureCreatedAsync();

        return new PermissionApiHost(server);
    }

    public async Task<string> LoginAsync(string usernameOrEmail, string password)
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { usernameOrEmail, password });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login response did not contain an access token.");
    }

    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    public async Task<(Guid UserId, Guid RoleId)> SeedUserAsync(
        string username,
        string password,
        bool isActive = true,
        params string[] permissionCodes)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
        var role = CreateRole(username + "_role");
        var user = new DynamicPermission.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            RoleId = role.Id,
            Username = username,
            Email = $"{username}@example.test",
            FullName = username,
            IsActive = isActive
        };
        user.PasswordHash = scope.ServiceProvider
            .GetRequiredService<IPasswordHasherService>()
            .HashPassword(user, password);

        db.Roles.Add(role);
        db.Users.Add(user);
        foreach (var code in permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permission = CreatePermission(code);
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new DynamicPermission.Domain.Entities.RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id
            });
        }

        await db.SaveChangesAsync();
        return (user.Id, role.Id);
    }

    public static DynamicPermission.Domain.Entities.Role CreateRole(string code, bool isActive = true)
    {
        return new DynamicPermission.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            IsActive = isActive
        };
    }

    public static DynamicPermission.Domain.Entities.Permission CreatePermission(string code, bool isActive = true)
    {
        var parts = code.Split('.', 2);
        return new DynamicPermission.Domain.Entities.Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Module = parts[0],
            Action = parts.Length == 2 ? parts[1] : code,
            IsActive = isActive
        };
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _server.Dispose();
        return ValueTask.CompletedTask;
    }
}
