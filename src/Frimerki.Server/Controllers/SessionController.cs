using Frimerki.Models.DTOs;
using Frimerki.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase {
    private readonly ISessionService _sessionService;
    private readonly ILogger<SessionController> _logger;

    public SessionController(ISessionService sessionService, ILogger<SessionController> logger) {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Create session (login)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Login request for email: {Email}", request.Email);

            var result = await _sessionService.LoginAsync(request);
            if (result == null) {
                return Unauthorized(new { error = "Invalid email or password" });
            }

            // Set HTTP-only cookie with refresh token (optional, for web clients)
            Response.Cookies.Append("refreshToken", result.RefreshToken, new CookieOptions {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(30)
            });

            return Ok(result);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during login for email: {Email}", request.Email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete session (logout)
    /// </summary>
    [HttpDelete]
    [Authorize]
    public async Task<ActionResult<LogoutResponse>> Logout() {
        try {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) {
                return BadRequest(new { error = "Invalid user session" });
            }

            _logger.LogInformation("Logout request for user ID: {UserId}", userId);

            var success = await _sessionService.LogoutAsync(userId);

            // Clear refresh token cookie
            Response.Cookies.Delete("refreshToken");

            return Ok(new LogoutResponse {
                Message = success ? "Logged out successfully" : "Logout completed",
                Success = success
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get current session/user info (auto-refreshes token)
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<SessionResponse>> GetSession() {
        try {
            _logger.LogInformation("Get session request for user: {Email}", User.FindFirst(ClaimTypes.Email)?.Value);

            var session = await _sessionService.GetCurrentSessionAsync(User);

            if (!session.IsAuthenticated) {
                return Unauthorized(new { error = "Session invalid or expired" });
            }

            return Ok(session);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting current session");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> RefreshToken([FromBody] RefreshTokenRequest? request) {
        try {
            var refreshToken = request?.RefreshToken;

            // If not provided in body, try to get from cookie
            if (string.IsNullOrEmpty(refreshToken)) {
                refreshToken = Request.Cookies["refreshToken"];
            }

            if (string.IsNullOrEmpty(refreshToken)) {
                return BadRequest(new { error = "Refresh token is required" });
            }

            _logger.LogInformation("Token refresh request");

            var result = await _sessionService.RefreshTokenAsync(refreshToken);
            if (result == null) {
                // Clear invalid refresh token cookie
                Response.Cookies.Delete("refreshToken");
                return Unauthorized(new { error = "Invalid or expired refresh token" });
            }

            // Update refresh token cookie
            Response.Cookies.Append("refreshToken", result.RefreshToken, new CookieOptions {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(30)
            });

            return Ok(result);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Revoke refresh token
    /// </summary>
    [HttpPost("revoke")]
    public async Task<ActionResult> RevokeToken([FromBody] RefreshTokenRequest? request) {
        try {
            var refreshToken = request?.RefreshToken;

            // If not provided in body, try to get from cookie
            if (string.IsNullOrEmpty(refreshToken)) {
                refreshToken = Request.Cookies["refreshToken"];
            }

            if (string.IsNullOrEmpty(refreshToken)) {
                return BadRequest(new { error = "Refresh token is required" });
            }

            _logger.LogInformation("Token revocation request");

            var success = await _sessionService.RevokeRefreshTokenAsync(refreshToken);

            // Clear refresh token cookie
            Response.Cookies.Delete("refreshToken");

            return Ok(new {
                message = success ? "Token revoked successfully" : "Token revocation completed",
                success = success
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during token revocation");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Check if user is authenticated (lightweight endpoint)
    /// </summary>
    [HttpGet("status")]
    public ActionResult GetAuthStatus() {
        try {
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            return Ok(new {
                isAuthenticated = isAuthenticated,
                email = email,
                role = role,
                timestamp = DateTime.UtcNow
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting auth status");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
