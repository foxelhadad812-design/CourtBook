using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns a consistent RFC 7807 ProblemDetails response.
/// This prevents raw stack traces from leaking to clients and ensures every error
/// has a predictable JSON shape that the frontend can handle uniformly.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (statusCode, title) = ex switch
        {
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access Denied"),
            ArgumentNullException       => (StatusCodes.Status400BadRequest, "Bad Request"),
            ArgumentException           => (StatusCodes.Status400BadRequest, "Bad Request"),
            InvalidOperationException   => (StatusCodes.Status409Conflict, "Conflict"),
            KeyNotFoundException        => (StatusCodes.Status404NotFound, "Not Found"),
            OperationCanceledException  => (StatusCodes.Status499ClientClosedRequest, "Request Cancelled"),
            _                           => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        // Only log 5xx as errors; 4xx as warnings.
        if (statusCode >= 500)
            _logger.LogError(ex, "Unhandled {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
        else
            _logger.LogWarning("Handled {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);

        var problem = new ProblemDetails
        {
            Status   = statusCode,
            Title    = title,
            Detail   = ex.Message,
            Instance = context.Request.Path,
        };

        // Only include the stack trace in Development to avoid leaking internals.
        if (_env.IsDevelopment())
            problem.Extensions["stackTrace"] = ex.StackTrace;

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.ContentType  = "application/problem+json";
        context.Response.StatusCode   = statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
