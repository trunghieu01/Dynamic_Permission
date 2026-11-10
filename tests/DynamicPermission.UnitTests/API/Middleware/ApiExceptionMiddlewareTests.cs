using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DynamicPermission.API.Middleware;
using DynamicPermission.Application.Common.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace DynamicPermission.UnitTests.API.Middleware;

public class ApiExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldReturnSafeProblemDetails_WhenConflictIsThrown()
    {
        var context = CreateContext();
        var logger = new TestLogger<ApiExceptionMiddleware>();
        var middleware = new ApiExceptionMiddleware(
            _ => throw new ConflictException("Sensitive conflict details."),
            logger);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.ContentType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(context);
        problem.Title.Should().Be("Conflict");
        problem.Detail.Should().NotContain("Sensitive");
        problem.Extensions["traceId"]!.ToString().Should().Be(context.Response.Headers["X-Correlation-ID"].ToString());
    }

    [Theory]
    [InlineData(StatusCodes.Status404NotFound, "Not Found")]
    [InlineData(StatusCodes.Status400BadRequest, "Bad Request")]
    [InlineData(StatusCodes.Status401Unauthorized, "Unauthorized")]
    [InlineData(StatusCodes.Status403Forbidden, "Forbidden")]
    public async Task InvokeAsync_ShouldMapKnownExceptionsWithoutExposingMessages(int statusCode, string title)
    {
        Exception exception = statusCode switch
        {
            StatusCodes.Status404NotFound => new KeyNotFoundException("Private resource identifier."),
            StatusCodes.Status400BadRequest => new ArgumentException("Private input details."),
            StatusCodes.Status401Unauthorized => new UnauthorizedAccessException("Private authentication detail."),
            _ => new System.Security.SecurityException("Private authorization detail.")
        };
        var context = CreateContext();
        var middleware = new ApiExceptionMiddleware(_ => throw exception, new TestLogger<ApiExceptionMiddleware>());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(statusCode);
        var responseBody = await ReadResponseTextAsync(context);
        responseBody.Should().NotContain("Private");
        var problem = JsonSerializer.Deserialize<ProblemDetails>(responseBody);
        problem!.Title.Should().Be(title);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotExposeUnexpectedExceptionDetails()
    {
        var context = CreateContext();
        var middleware = new ApiExceptionMiddleware(
            _ => throw new Exception("sensitive database information"),
            new TestLogger<ApiExceptionMiddleware>());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = await ReadProblemAsync(context);
        problem.Detail.Should().Be("An unexpected error occurred.");
        (await ReadResponseTextAsync(context)).Should().NotContain("sensitive");
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogStructuredRequestAndRedactedBodies_WithoutConsumingRequest()
    {
        var context = CreateContext();
        var requestBody = """{"email":"user@example.com","password":"request-secret","refresh_token":"refresh-secret"}""";
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/login";
        context.Request.QueryString = new QueryString("?token=query-secret");
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        context.Request.ContentLength = Encoding.UTF8.GetByteCount(requestBody);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "user-123") },
            authenticationType: "test"));
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/auth/login"),
            0,
            new EndpointMetadataCollection(),
            "Auth.Login"));
        var logger = new TestLogger<ApiExceptionMiddleware>();
        var middleware = new ApiExceptionMiddleware(
            async httpContext =>
            {
                using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                (await reader.ReadToEndAsync()).Should().Be(requestBody);
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(
                    """{"accessToken":"response-secret","result":"ok"}""");
            },
            logger);

        await middleware.InvokeAsync(context);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["Method"].Should().Be(HttpMethods.Post);
        entry.Properties["RequestPath"].Should().Be("/api/auth/login");
        entry.Properties["RequestId"].Should().NotBeNull();
        entry.Properties["CorrelationId"].Should().Be(context.Response.Headers["X-Correlation-ID"].ToString());
        entry.Properties["UserId"].Should().Be("user-123");
        entry.Properties["StatusCode"].Should().Be(StatusCodes.Status200OK);
        entry.Properties["RequestBodyCapture"].Should().Be("Captured");
        entry.Properties["ResponseBodyCapture"].Should().Be("Captured");
        entry.Properties["RequestBody"]!.ToString().Should().Contain("[REDACTED]");
        entry.Properties["RequestBody"]!.ToString().Should().NotContain("request-secret");
        entry.Properties["RequestBody"]!.ToString().Should().NotContain("refresh-secret");
        entry.Properties["ResponseBody"]!.ToString().Should().Contain("[REDACTED]");
        entry.Properties["ResponseBody"]!.ToString().Should().NotContain("response-secret");
        string.Join("|", entry.Properties.Values).Should().NotContain("query-secret");
        context.Response.Headers["X-Correlation-ID"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSkipLargeBodiesAndPreserveResponse()
    {
        var context = CreateContext();
        var requestBody = $"{{\"value\":\"{new string('x', 20_000)}\"}}";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        context.Request.ContentLength = Encoding.UTF8.GetByteCount(requestBody);
        var logger = new TestLogger<ApiExceptionMiddleware>();
        const string responseBody = """{"ok":true}""";
        var middleware = new ApiExceptionMiddleware(
            httpContext =>
            {
                httpContext.Response.ContentType = "application/json";
                return httpContext.Response.WriteAsync(responseBody);
            },
            logger);

        await middleware.InvokeAsync(context);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Properties["RequestBodyCapture"].Should().Be("SkippedTooLarge");
        entry.Properties["RequestBody"].Should().BeNull();
        (await ReadResponseTextAsync(context)).Should().Be(responseBody);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotLogRequestCancellationAsAnApplicationError()
    {
        var context = CreateContext();
        context.RequestAborted = new CancellationToken(canceled: true);
        var logger = new TestLogger<ApiExceptionMiddleware>();
        var middleware = new ApiExceptionMiddleware(
            _ => throw new OperationCanceledException(context.RequestAborted),
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        logger.Entries.Should().BeEmpty();
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<ProblemDetails> ReadProblemAsync(HttpContext context)
    {
        return JsonSerializer.Deserialize<ProblemDetails>(await ReadResponseTextAsync(context))
            ?? throw new InvalidOperationException("Problem Details response was empty.");
    }

    private static async Task<string> ReadResponseTextAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
