using System.Security.Claims;
using Frimerki.Models.DTOs;
using Frimerki.Services.Email;
using Frimerki.Services.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmailController(
    SmtpClientService smtpClientService,
    IUserService userService,
    ILogger<EmailController> logger)
    : ControllerBase {
    /// <summary>
    /// Send an email message
    /// </summary>
    [HttpPost("send")]
    public async Task<ActionResult> SendEmail([FromBody] MessageRequest request) {
        try {
            // Get the authenticated user's email address
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) {
                return BadRequest("User email not found in token");
            }

            // Validate user exists and is active
            var user = await userService.GetUserByEmailAsync(userEmail);
            if (user == null) {
                return Unauthorized("User not found");
            }

            // Send the email
            var success = await smtpClientService.SendEmailAsync(request, userEmail);

            if (success) {
                logger.LogInformation("Email sent successfully from {From} to {To}",
                    userEmail, request.ToAddress);
                return Ok(new { message = "Email sent successfully" });
            } else {
                logger.LogWarning("Failed to send email from {From} to {To}",
                    userEmail, request.ToAddress);
                return StatusCode(500, new { message = "Failed to send email" });
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error sending email");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Send a simple text email
    /// </summary>
    [HttpPost("send/simple")]
    public async Task<ActionResult> SendSimpleEmail([FromBody] SimpleEmailRequest request) {
        try {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) {
                return BadRequest("User email not found in token");
            }

            var user = await userService.GetUserByEmailAsync(userEmail);
            if (user == null) {
                return Unauthorized("User not found");
            }

            var success = await smtpClientService.SendSimpleEmailAsync(
                userEmail, request.To, request.Subject, request.Body);

            if (success) {
                return Ok(new { message = "Email sent successfully" });
            } else {
                return StatusCode(500, new { message = "Failed to send email" });
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error sending simple email");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Get SMTP configuration status
    /// </summary>
    [HttpGet("config/status")]
    public ActionResult GetConfigurationStatus() {
        try {
            var isValid = smtpClientService.ValidateConfiguration();
            return Ok(new {
                isConfigured = isValid,
                message = isValid ? "SMTP configuration is valid" : "SMTP configuration is invalid or missing"
            });
        } catch (Exception ex) {
            logger.LogError(ex, "Error checking SMTP configuration");
            return StatusCode(500, new { message = "Error checking configuration" });
        }
    }
}
