using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Tubester.Application.Common;

namespace Tubester.Api.Infrastructure;

/// <summary>
/// 
/// </summary>
/// <param name="problemDetailsFactory"></param>
/// <param name="environment"></param>
/// <param name="logger"></param>
public sealed class GlobalExceptionHandler(
    ProblemDetailsFactory problemDetailsFactory,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="httpContext"></param>
    /// <param name="exception"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = Map(exception);

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var pd = problemDetailsFactory.CreateProblemDetails(
            httpContext,
            status,
            title,
            detail: detail);

        // Always include a trace id for correlation
        pd.Extensions["traceId"] = httpContext.TraceIdentifier;

        // Optional: include exception details in Development
        if (environment.IsDevelopment())
        {
            pd.Extensions["exception"] = new
            {
                type = exception.GetType().FullName,
                message = exception.Message,
                stackTrace = exception.StackTrace
            };
        }

        // Log at sensible levels
        var level = status >= 500 ? LogLevel.Error : LogLevel.Warning;

        logger.Log(level, exception, "{Title} ({Status}). TraceId={TraceId}", title, status,
            httpContext.TraceIdentifier);

        await httpContext.Response.WriteAsJsonAsync(pd, ct);
        return true;
    }

    private static (int status, string title, string? detail) Map(Exception ex)
    {
        return ex switch
        {
            NotFoundException nf
                => (StatusCodes.Status404NotFound, "Not Found", nf.Message),

            ForbiddenException fb
                => (StatusCodes.Status403Forbidden, "Forbidden", fb.Message),

            ConflictException cf
                => (StatusCodes.Status409Conflict, "Conflict", cf.Message),

            UnauthorizedAccessException
                => (StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required."),

            OperationCanceledException
                => (StatusCodes.Status400BadRequest, "Request cancelled", "The operation was cancelled by the client."),

            DbUpdateConcurrencyException
                => (StatusCodes.Status409Conflict, "Concurrency conflict",
                    "The resource was modified by another process. Try again."),

            DbUpdateException due when IsUniqueConstraintViolation(due)
                => (StatusCodes.Status409Conflict, "Unique constraint violated",
                    "A resource with the same unique value already exists."),

            BadRequestException br
                => (StatusCodes.Status400BadRequest, "Bad request", br.Message),

            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.")
        };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("23505", StringComparison.OrdinalIgnoreCase) // PostgreSQL
               || msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase); // SQLite
    }
}