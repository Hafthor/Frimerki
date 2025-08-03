using System.Security.Claims;
using Frimerki.Models.DTOs;
using Frimerki.Services.Message;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController(IMessageService messageService, ILogger<MessagesController> logger)
    : ControllerBase {
    /// <summary>
    /// Get messages with filtering and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedInfo<MessageListItemResponse>>> GetMessages([FromQuery] MessageFilterRequest request) {
        try {
            var userId = GetCurrentUserId();
            logger.LogInformation("Getting messages for user {UserId} with filters", userId);

            var result = await messageService.GetMessagesAsync(userId, request);
            return Ok(result);
        } catch (Exception ex) {
            logger.LogError(ex, "Error getting messages");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get a specific message by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<MessageResponse>> GetMessage(int id) {
        try {
            var userId = GetCurrentUserId();
            logger.LogInformation("Getting message {MessageId} for user {UserId}", id, userId);

            var message = await messageService.GetMessageAsync(userId, id);
            if (message == null) {
                return NotFound(new { error = "Message not found" });
            }

            return Ok(message);
        } catch (Exception ex) {
            logger.LogError(ex, "Error getting message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error" });
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
            logger.LogInformation("Creating message for user {UserId}", userId);

            var message = await messageService.CreateMessageAsync(userId, request);
            return CreatedAtAction(nameof(GetMessage), new { id = message.Id }, message);
        } catch (ArgumentException ex) {
            logger.LogWarning(ex, "Invalid request for creating message");
            return BadRequest(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            logger.LogError(ex, "Error creating message");
            return StatusCode(500, new { error = "Internal server error" });
        } catch (Exception ex) {
            logger.LogError(ex, "Unexpected error creating message");
            return StatusCode(500, new { error = "Internal server error" });
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
            logger.LogInformation("Updating message {MessageId} for user {UserId}", id, userId);

            var message = await messageService.UpdateMessageAsync(userId, id, request);
            if (message == null) {
                return NotFound(new { error = "Message not found" });
            }

            return Ok(message);
        } catch (ArgumentException ex) {
            logger.LogWarning(ex, "Invalid request for updating message {MessageId}", id);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error updating message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error" });
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
            logger.LogInformation("Patching message {MessageId} for user {UserId}", id, userId);

            var message = await messageService.UpdateMessageAsync(userId, id, request);
            if (message == null) {
                return NotFound(new { error = "Message not found" });
            }

            return Ok(message);
        } catch (ArgumentException ex) {
            logger.LogWarning(ex, "Invalid request for patching message {MessageId}", id);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error patching message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete a message (move to trash)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMessage(int id) {
        try {
            var userId = GetCurrentUserId();
            logger.LogInformation("Deleting message {MessageId} for user {UserId}", id, userId);

            var result = await messageService.DeleteMessageAsync(userId, id);
            if (!result) {
                return NotFound(new { error = "Message not found" });
            }

            return NoContent();
        } catch (Exception ex) {
            logger.LogError(ex, "Error deleting message {MessageId}", id);
            return StatusCode(500, new { error = "Internal server error" });
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
