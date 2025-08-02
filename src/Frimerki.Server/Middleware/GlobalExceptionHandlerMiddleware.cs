using System.Net;
using System.Text.Json;

namespace Frimerki.Server.Middleware;

/// <summary>
/// Global exception handling middleware to prevent stack trace exposure
/// </summary>
public class GlobalExceptionHandlerMiddleware {
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger, IWebHostEnvironment environment) {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context) {
        try {
            await _next(context);
        } catch (Exception ex) {
            _logger.LogError(ex, "Unhandled exception occurred for request {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception) {
        context.Response.ContentType = "application/json";

        object errorResponse;

        // Only include exception details in development environment
        if (_environment.IsDevelopment()) {
            errorResponse = new {
                error = "Internal server error",
                message = exception.Message,
                details = exception.ToString(),
                timestamp = DateTime.UtcNow,
                requestId = context.TraceIdentifier
            };
        } else {
            errorResponse = new {
                error = "Internal server error",
                timestamp = DateTime.UtcNow,
                requestId = context.TraceIdentifier
            };
        }

        var statusCode = exception switch {
            ArgumentException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            FileNotFoundException => (int)HttpStatusCode.NotFound,
            InvalidOperationException => (int)HttpStatusCode.Conflict,
            _ => (int)HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = statusCode;

        var jsonResponse = JsonSerializer.Serialize(errorResponse);
        await context.Response.WriteAsync(jsonResponse);
    }
}
