using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

/// <summary>
/// Base controller with common functionality for error handling
/// </summary>
public abstract class ApiControllerBase : ControllerBase {

    /// <summary>
    /// Returns a safe error response that doesn't expose internal details
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="logger">Logger for recording the error</param>
    /// <param name="context">Context information for logging</param>
    /// <returns>ActionResult with appropriate status code and safe error message</returns>
    protected ActionResult HandleException(Exception exception, ILogger logger, string context) {
        var statusCode = exception switch {
            ArgumentException => 400,
            UnauthorizedAccessException => 401,
            FileNotFoundException => 404,
            InvalidOperationException => 409,
            _ => 500
        };

        var safeMessage = exception switch {
            ArgumentException ex => ex.Message, // ArgumentException messages are typically safe
            UnauthorizedAccessException => "Access denied",
            FileNotFoundException => "Resource not found",
            InvalidOperationException => "Operation cannot be completed",
            _ => "Internal server error"
        };

        logger.LogError(exception, "Error in {Context}", context);

        return StatusCode(statusCode, new { error = safeMessage });
    }

    /// <summary>
    /// Returns a safe error response for validation/business logic exceptions
    /// where the message is intended for the user
    /// </summary>
    /// <param name="exception">The business logic exception</param>
    /// <param name="logger">Logger for recording the warning</param>
    /// <param name="context">Context information for logging</param>
    /// <returns>BadRequest with the exception message</returns>
    protected ActionResult HandleBusinessException(Exception exception, ILogger logger, string context) {
        logger.LogWarning(exception, "Business logic error in {Context}: {Message}", context, exception.Message);
        return BadRequest(new { error = exception.Message });
    }
}
