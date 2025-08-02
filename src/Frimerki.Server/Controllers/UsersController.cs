using System.Security.Claims;
using Frimerki.Models.DTOs;
using Frimerki.Services.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UsersController : ControllerBase {
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger) {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// List all users (HostAdmin) or domain users (DomainAdmin)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedInfo<UserResponse>>> GetUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? domain = null) {
        try {
            if (skip < 0) {
                skip = 0;
            }
            if (take is < 1 or > 100) {
                take = 50;
            }

            _logger.LogInformation("Getting users list - Skip: {Skip}, Take: {Take}, Domain: {Domain}",
                skip, take, domain ?? "All");

            var result = await _userService.GetUsersAsync(skip, take, domain);
            return Ok(result);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting users list");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Create new user (HostAdmin/DomainAdmin)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser([FromBody] CreateUserRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Creating user: {Username}@{Domain}", request.Username, request.DomainName);

            var user = await _userService.CreateUserAsync(request);
            return CreatedAtAction(nameof(GetUser), new { email = $"{request.Username}@{request.DomainName}" }, user);
        } catch (ArgumentException ex) {
            _logger.LogWarning("User creation failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error creating user: {Username}@{Domain}", request.Username, request.DomainName);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get user details (own account or admin), returns 404 if user doesn't exist
    /// </summary>
    [HttpGet("{email}")]
    public async Task<ActionResult> GetUser(string email) {
        try {
            _logger.LogInformation("Getting user details: {Email}", email);

            var user = await _userService.GetUserByEmailAsync(email);
            if (user == null) {
                return NotFound();
            }

            // Check if current user is admin or accessing their own account
            var currentUserEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;

            bool isAdmin = currentUserRole == "HostAdmin" || currentUserRole == "DomainAdmin";
            bool isOwnAccount = currentUserEmail == email;

            if (isAdmin || isOwnAccount) {
                // Return full user details for admins or own account
                return Ok(user);
            }

            // Return minimal info for non-admin users accessing other accounts
            var minimalResponse = new UserMinimalResponse {
                Email = user.Email,
                Username = user.Username
            };
            return Ok(minimalResponse);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting user: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update user (own account or admin)
    /// </summary>
    [HttpPut("{email}")]
    public async Task<ActionResult<UserResponse>> UpdateUser(string email, [FromBody] UserUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Updating user: {Email}", email);

            var user = await _userService.UpdateUserAsync(email, request);
            if (user == null) {
                return NotFound(new { error = $"User '{email}' not found" });
            }

            return Ok(user);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating user: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Partial update user including password (own account or admin)
    /// </summary>
    [HttpPatch("{email}")]
    public async Task<ActionResult<UserResponse>> PatchUser(string email, [FromBody] UserUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Patching user: {Email}", email);

            var user = await _userService.UpdateUserAsync(email, request);
            if (user == null) {
                return NotFound(new { error = $"User '{email}' not found" });
            }

            return Ok(user);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error patching user: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update user password
    /// </summary>
    [HttpPatch("{email}/password")]
    public async Task<ActionResult> UpdateUserPassword(string email, [FromBody] UserPasswordUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Updating password for user: {Email}", email);

            var success = await _userService.UpdateUserPasswordAsync(email, request);
            if (!success) {
                return NotFound(new { error = $"User '{email}' not found" });
            }

            return Ok(new { message = "Password updated successfully" });
        } catch (UnauthorizedAccessException ex) {
            _logger.LogWarning("Password update failed for user {Email}: {Message}", email, ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating password for user: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete user (HostAdmin/DomainAdmin)
    /// </summary>
    [HttpDelete("{email}")]
    public async Task<ActionResult> DeleteUser(string email) {
        try {
            _logger.LogInformation("Deleting user: {Email}", email);

            var success = await _userService.DeleteUserAsync(email);
            if (!success) {
                return NotFound(new { error = $"User '{email}' not found" });
            }

            return Ok(new { message = $"User '{email}' deleted successfully" });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error deleting user: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get user statistics
    /// </summary>
    [HttpGet("{email}/stats")]
    public async Task<ActionResult<UserStatsResponse>> GetUserStats(string email) {
        try {
            _logger.LogInformation("Getting stats for user: {Email}", email);

            var stats = await _userService.GetUserStatsAsync(email);
            return Ok(stats);
        } catch (ArgumentException ex) {
            _logger.LogWarning("Get user stats failed: {Message}", ex.Message);
            return NotFound(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting stats for user: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
