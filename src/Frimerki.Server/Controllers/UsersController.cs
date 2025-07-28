using Frimerki.Models.DTOs;
using Frimerki.Services.User;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Frimerki.Server.Controllers;

[ApiController]
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
    public async Task<ActionResult<UserListResponse>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? domain = null) {
        try {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 50;

            _logger.LogInformation("Getting users list - Page: {Page}, PageSize: {PageSize}, Domain: {Domain}",
                page, pageSize, domain ?? "All");

            var result = await _userService.GetUsersAsync(page, pageSize, domain);
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
    /// Get user details (own account or admin)
    /// </summary>
    [HttpGet("{email}")]
    public async Task<ActionResult<UserResponse>> GetUser(string email) {
        try {
            _logger.LogInformation("Getting user details: {Email}", email);

            var user = await _userService.GetUserByEmailAsync(email);
            if (user == null) {
                return NotFound(new { error = $"User '{email}' not found" });
            }

            return Ok(user);
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

    /// <summary>
    /// Validate email format and availability
    /// </summary>
    [HttpGet("validate/{email}")]
    public async Task<ActionResult> ValidateUser(string email) {
        try {
            _logger.LogInformation("Validating email: {Email}", email);

            var isValidFormat = await _userService.ValidateEmailFormatAsync(email);
            if (!isValidFormat) {
                return Ok(new {
                    isValid = false,
                    isAvailable = false,
                    message = "Invalid email format"
                });
            }

            var exists = await _userService.UserExistsAsync(email);
            return Ok(new {
                isValid = true,
                isAvailable = !exists,
                message = exists ? "Email address already in use" : "Email address is available"
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error validating email: {Email}", email);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Validate username for a specific domain
    /// </summary>
    [HttpGet("validate/{username}/domain/{domainName}")]
    public async Task<ActionResult> ValidateUsername(string username, string domainName) {
        try {
            _logger.LogInformation("Validating username: {Username} for domain: {Domain}", username, domainName);

            var isValid = await _userService.ValidateUsernameAsync(username, domainName);
            return Ok(new {
                isValid = isValid,
                message = isValid ? "Username is available" : "Username is invalid or already taken"
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error validating username: {Username}@{Domain}", username, domainName);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
