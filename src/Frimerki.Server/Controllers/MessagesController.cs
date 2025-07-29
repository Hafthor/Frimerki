using System.Security.Claims;
using Frimerki.Models.DTOs;
using Frimerki.Services.Message;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase {
    private readonly IMessageService _messageService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMessageService messageService, ILogger<MessagesController> logger) {
        _messageService = messageService;
        _logger = logger;
    }

    /// <summary>
    /// Get messages with filtering and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MessageListResponse>> GetMessages([FromQuery] MessageFilterRequest request) {
        try {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Getting messages for user {UserId} with filters", userId);

            var result = await _messageService.GetMessagesAsync(userId, request);
            return Ok(result);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting messages");
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific message by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<MessageResponse>> GetMessage(int id) {
        try {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Getting message {MessageId} for user {UserId}", id, userId);

            var message = await _messageService.GetMessageAsync(userId, id);
            if (message == null) {
                return NotFound(new { error = "Message not found" });
            }

            return Ok(message);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Send a new message
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MessageResponse>> CreateMessage([FromBody] MessageRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            _logger.LogInformation("Creating message for user {UserId}", userId);

            var message = await _messageService.CreateMessageAsync(userId, request);
            return CreatedAtAction(nameof(GetMessage), new { id = message.Id }, message);
        } catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid request for creating message");
            return BadRequest(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            _logger.LogError(ex, "Error creating message");
            return StatusCode(500, new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error creating message");
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Update a message (flags, folder, content for drafts)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<MessageResponse>> UpdateMessage(int id, [FromBody] MessageUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            _logger.LogInformation("Updating message {MessageId} for user {UserId}", id, userId);

            var message = await _messageService.UpdateMessageAsync(userId, id, request);
            if (message == null) {
                return NotFound(new { error = "Message not found" });
            }

            return Ok(message);
        } catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid request for updating message {MessageId}", id);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Partially update a message (flags, folder, content for drafts)
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<MessageResponse>> PatchMessage(int id, [FromBody] MessageUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            _logger.LogInformation("Patching message {MessageId} for user {UserId}", id, userId);

            var message = await _messageService.UpdateMessageAsync(userId, id, request);
            if (message == null) {
                return NotFound(new { error = "Message not found" });
            }

            return Ok(message);
        } catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid request for patching message {MessageId}", id);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error patching message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete a message (move to trash)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMessage(int id) {
        try {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Deleting message {MessageId} for user {UserId}", id, userId);

            var result = await _messageService.DeleteMessageAsync(userId, id);
            if (!result) {
                return NotFound(new { error = "Message not found" });
            }

            return NoContent();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error deleting message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    private int GetCurrentUserId() {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId)) {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }
}
