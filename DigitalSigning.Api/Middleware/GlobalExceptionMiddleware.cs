using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace DigitalSigning.Api.Middleware;

/// <summary>
/// Global exception middleware — catches unhandled exceptions and returns
/// a structured JSON error response.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — no need to log as error
            _logger.LogInformation("Request cancelled by client: {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 499; // 499 = Client Closed Request (nginx convention)

            var response = new
            {
                errorCode = Core.Enums.ErrorCode.Timeout.ToString(),
                message = "Request was cancelled by the client",
                traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(response);
        }
        catch (Exception ex)
        {
            var isDevelopment = context.RequestServices
                .GetService<IWebHostEnvironment>()?.IsDevelopment() == true;

            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                errorCode = Core.Enums.ErrorCode.UnexpectedException.ToString(),
                message = "An unexpected error occurred",
                details = isDevelopment
                    ? ex.Message
                    : null,
                traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}

/// <summary>
/// Extension to register the middleware.
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalException(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
