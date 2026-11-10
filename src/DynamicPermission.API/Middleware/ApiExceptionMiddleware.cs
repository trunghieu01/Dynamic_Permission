using System.Diagnostics;
using System.Security;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DynamicPermission.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;

namespace DynamicPermission.API.Middleware;

public sealed class ApiExceptionMiddleware
{
    private const int MaxBodyCaptureBytes = 16 * 1024;
    private const string CorrelationHeader = "X-Correlation-ID";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SensitiveJsonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwordhash", "pwd", "token", "accesstoken", "refreshtoken",
        "authorization", "cookie", "secret", "apikey", "clientsecret", "credential",
        "email", "username", "firstname", "lastname", "phone", "mobile", "address",
        "fullname", "newpassword", "initialpassword", "dateofbirth", "nationalid",
        "ssn", "cardnumber", "accountnumber", "bankaccount"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers[CorrelationHeader] = traceId;

        var (requestBody, requestCaptureStatus) = await CaptureRequestBodyAsync(context.Request);
        var originalResponseBody = context.Response.Body;
        using var responseCapture = new BoundedResponseCaptureStream(originalResponseBody, MaxBodyCaptureBytes);
        context.Response.Body = responseCapture;

        Exception? requestException = null;
        var requestWasCancelled = false;

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            requestWasCancelled = true;
            throw;
        }
        catch (Exception exception)
        {
            requestException = exception;
            if (context.Response.HasStarted)
            {
                throw;
            }

            var problem = CreateProblemDetails(exception, context.Request.Path, traceId);
            context.Response.Clear();
            context.Response.Headers[CorrelationHeader] = traceId;
            context.Response.StatusCode = problem.Status!.Value;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                problem,
                JsonOptions,
                context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalResponseBody;

            if (!requestWasCancelled)
            {
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                var routeEndpoint = context.GetEndpoint() as RouteEndpoint;
                var endpoint = routeEndpoint is not null
                    ? routeEndpoint.RoutePattern.RawText ?? "unknown"
                    : context.GetEndpoint()?.DisplayName ?? "unmatched";
                var requestPath = routeEndpoint?.RoutePattern.RawText ?? "unmatched";
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.User.FindFirstValue("sub")
                    ?? "anonymous";
                var (responseBody, responseCaptureStatus) = CaptureResponseBody(
                    responseCapture,
                    context.Response.ContentType);

                var logLevel = requestException is not null
                    ? context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                        ? LogLevel.Error
                        : LogLevel.Warning
                    : context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                        ? LogLevel.Error
                        : context.Response.StatusCode >= StatusCodes.Status400BadRequest
                            ? LogLevel.Warning
                            : LogLevel.Information;
                var exceptionForLog = context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? requestException
                    : null;

                _logger.Log(
                    logLevel,
                    exceptionForLog,
                    "HTTP request {Method} {Endpoint} responded with {StatusCode} for {UserId} in {ElapsedMilliseconds} ms. RequestId: {RequestId}; CorrelationId: {CorrelationId}; RequestPath: {RequestPath}; RequestContentType: {RequestContentType}; ResponseContentType: {ResponseContentType}; RequestBodyCapture: {RequestBodyCapture}; RequestBody: {RequestBody}; ResponseBodyCapture: {ResponseBodyCapture}; ResponseBody: {ResponseBody}",
                    context.Request.Method,
                    endpoint,
                    context.Response.StatusCode,
                    userId,
                    elapsedMilliseconds,
                    context.TraceIdentifier,
                    traceId,
                    requestPath,
                    context.Request.ContentType ?? string.Empty,
                    context.Response.ContentType ?? string.Empty,
                    requestCaptureStatus,
                    requestBody,
                    responseCaptureStatus,
                    responseBody);
            }
        }
    }

    private static ProblemDetails CreateProblemDetails(Exception exception, PathString path, string traceId)
    {
        var (statusCode, title, detail) = MapException(exception);
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = path
        };
        problem.Extensions["traceId"] = traceId;
        return problem;
    }

    private static (int StatusCode, string Title, string Detail) MapException(Exception exception)
    {
        if (exception is KeyNotFoundException)
        {
            return (StatusCodes.Status404NotFound, "Not Found", "The requested resource was not found.");
        }

        if (exception is ArgumentException)
        {
            return (StatusCodes.Status400BadRequest, "Bad Request", "The request is invalid.");
        }

        if (exception is UnauthorizedAccessException)
        {
            return (StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.");
        }

        if (exception is SecurityException)
        {
            return (StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to perform this operation.");
        }

        if (exception is ConflictException
            || exception is DbUpdateException updateException && IsUniqueConstraintViolation(updateException))
        {
            return (StatusCodes.Status409Conflict, "Conflict", "The request conflicts with the current resource state.");
        }

        return (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.");
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number is 2601 or 2627)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<(string? Body, string Status)> CaptureRequestBodyAsync(HttpRequest request)
    {
        if (!IsJson(request.ContentType))
        {
            return (null, "SkippedUnsupportedContentType");
        }

        if (!request.ContentLength.HasValue)
        {
            return (null, "SkippedUnknownLength");
        }

        if (request.ContentLength.Value > MaxBodyCaptureBytes)
        {
            return (null, "SkippedTooLarge");
        }

        if (request.ContentLength.Value == 0)
        {
            return (null, "Empty");
        }

        request.EnableBuffering(bufferThreshold: MaxBodyCaptureBytes);
        var originalPosition = request.Body.Position;
        try
        {
            request.Body.Position = 0;
            var bodyBuffer = new byte[(int)request.ContentLength.Value];
            var bytesRead = 0;
            while (bytesRead < bodyBuffer.Length)
            {
                var read = await request.Body.ReadAsync(
                    bodyBuffer.AsMemory(bytesRead),
                    request.HttpContext.RequestAborted);
                if (read == 0)
                {
                    return (null, "SkippedIncompleteBody");
                }

                bytesRead += read;
            }

            var body = Encoding.UTF8.GetString(bodyBuffer);
            var sanitizedBody = SanitizeJson(body);
            return sanitizedBody is null
                ? (null, "SkippedInvalidJson")
                : (sanitizedBody, "Captured");
        }
        finally
        {
            request.Body.Position = originalPosition;
        }
    }

    private static (string? Body, string Status) CaptureResponseBody(
        BoundedResponseCaptureStream capture,
        string? contentType)
    {
        if (!IsJson(contentType))
        {
            return (null, "SkippedUnsupportedContentType");
        }

        if (capture.WasTruncated)
        {
            return (null, "SkippedTooLarge");
        }

        if (capture.Length == 0)
        {
            return (null, "Empty");
        }

        var body = Encoding.UTF8.GetString(capture.GetCapturedBytes());
        var sanitizedBody = SanitizeJson(body);
        return sanitizedBody is null
            ? (null, "SkippedInvalidJson")
            : (sanitizedBody, "Captured");
    }

    private static bool IsJson(string? contentType)
    {
        return contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? SanitizeJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteSanitizedValue(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteSanitizedValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveProperty(property.Name))
                    {
                        writer.WriteStringValue("[REDACTED]");
                    }
                    else
                    {
                        WriteSanitizedValue(writer, property.Value);
                    }
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteSanitizedValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string NormalizePropertyName(string name)
    {
        return string.Concat(name.Where(char.IsLetterOrDigit));
    }

    private static bool IsSensitiveProperty(string name)
    {
        var normalizedName = NormalizePropertyName(name);
        return SensitiveJsonProperties.Contains(normalizedName)
            || normalizedName.EndsWith("token", StringComparison.OrdinalIgnoreCase)
            || normalizedName.EndsWith("password", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BoundedResponseCaptureStream : Stream
    {
        private readonly Stream _destination;
        private readonly int _captureLimit;
        private readonly MemoryStream _captured = new();

        public BoundedResponseCaptureStream(Stream destination, int captureLimit)
        {
            _destination = destination;
            _captureLimit = captureLimit;
        }

        public bool WasTruncated { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _captured.Length;

        public override long Position
        {
            get => _captured.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _destination.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _destination.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _destination.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            await _destination.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await _destination.WriteAsync(buffer, cancellationToken);
        }

        public byte[] GetCapturedBytes() => _captured.ToArray();

        private void Capture(ReadOnlySpan<byte> bytes)
        {
            var remaining = _captureLimit - (int)_captured.Length;
            var captureLength = Math.Min(remaining, bytes.Length);
            if (captureLength > 0)
            {
                _captured.Write(bytes[..captureLength]);
            }

            if (captureLength < bytes.Length)
            {
                WasTruncated = true;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captured.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
