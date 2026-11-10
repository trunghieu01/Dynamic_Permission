using System.Diagnostics;
using DynamicPermission.Application;
using DynamicPermission.Infrastructure;
using DynamicPermission.API.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

SelfLog.Enable(TextWriter.Synchronized(Console.Error));

var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "DynamicPermission.API")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

var seqServerUrl = builder.Configuration["Seq:ServerUrl"];
if (Uri.TryCreate(seqServerUrl, UriKind.Absolute, out var seqUri)
    && (seqUri.Scheme == Uri.UriSchemeHttp || seqUri.Scheme == Uri.UriSchemeHttps))
{
    loggerConfiguration.WriteTo.Async(
        sink => sink.Seq(
            seqUri.ToString(),
            apiKey: builder.Configuration["Seq:ApiKey"],
            batchPostingLimit: 100,
            period: TimeSpan.FromSeconds(2)),
        bufferSize: builder.Configuration.GetValue("Seq:BufferSize", 10000),
        blockWhenFull: false);
}
else
{
    SelfLog.WriteLine("Seq logging is disabled because Seq:ServerUrl is missing or invalid.");
}

Log.Logger = loggerConfiguration.CreateLogger();
builder.Host.UseSerilog();

// Add services to the container (Clean Architecture DI)
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = actionContext =>
    {
        var httpContext = actionContext.HttpContext;
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = "The request is invalid.",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "DynamicPermission API", 
        Version = "v1",
        Description = "Hệ thống quản lý phân quyền động (Dynamic Permission) với xác thực JWT Bearer"
    });

    // Add JWT Bearer Security Definition
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập JWT token theo định dạng: Bearer {your_token_here}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DynamicPermission API v1");
    });
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseMiddleware<ApiExceptionMiddleware>();

// Authentication MUST be before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
