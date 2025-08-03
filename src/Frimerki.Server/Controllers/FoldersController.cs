using System.Security.Claims;
using Frimerki.Models.DTOs;
using Frimerki.Services.Folder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoldersController(IFolderService folderService, ILogger<FoldersController> logger)
    : ControllerBase {
    /// <summary>
    /// List user folders with hierarchy (includes subscribed flag)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<FolderListResponse>>> GetFolders() {
        try {
            var userId = GetCurrentUserId();
            var folders = await folderService.GetFoldersAsync(userId);
            return Ok(folders);
        } catch (UnauthorizedAccessException ex) {
            logger.LogWarning("Unauthorized access attempt: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error getting folders");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get folder details and status (EXISTS, RECENT, UNSEEN, UIDNEXT, UIDVALIDITY, subscribed)
    /// </summary>
    [HttpGet("{name}")]
    public async Task<ActionResult<FolderResponse>> GetFolder(string name) {
        try {
            var userId = GetCurrentUserId();
            var decodedName = Uri.UnescapeDataString(name);
            var folder = await folderService.GetFolderAsync(userId, decodedName);
            return folder == null ? NotFound() : Ok(folder);
        } catch (UnauthorizedAccessException ex) {
            logger.LogWarning("Unauthorized access attempt: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error getting folder {FolderName}", name);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Create new folder
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<FolderResponse>> CreateFolder(FolderRequest request) {
        try {
            var userId = GetCurrentUserId();
            var folder = await folderService.CreateFolderAsync(userId, request);
            return CreatedAtAction(nameof(GetFolder), new { name = Uri.EscapeDataString(folder.Name) }, folder);
        } catch (UnauthorizedAccessException ex) {
            logger.LogWarning("Unauthorized access attempt: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            logger.LogWarning("Invalid folder creation request: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error creating folder {FolderName}", request.Name);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update folder (rename, subscription status, etc.)
    /// </summary>
    [HttpPut("{name}")]
    public async Task<ActionResult<FolderResponse>> UpdateFolder(string name, FolderUpdateRequest request) {
        try {
            var userId = GetCurrentUserId();
            var decodedName = Uri.UnescapeDataString(name);
            var folder = await folderService.UpdateFolderAsync(userId, decodedName, request);
            return folder == null ? NotFound() : Ok(folder);
        } catch (UnauthorizedAccessException ex) {
            logger.LogWarning("Unauthorized access attempt: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            logger.LogWarning("Invalid folder update request: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error updating folder {FolderName}", name);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Partially update folder (rename, subscription status, etc.)
    /// </summary>
    [HttpPatch("{name}")]
    public async Task<ActionResult<FolderResponse>> PatchFolder(string name, FolderUpdateRequest request) {
        try {
            var userId = GetCurrentUserId();
            var decodedName = Uri.UnescapeDataString(name);
            var folder = await folderService.UpdateFolderAsync(userId, decodedName, request);
            return folder == null ? NotFound() : Ok(folder);
        } catch (UnauthorizedAccessException ex) {
            logger.LogWarning("Unauthorized access attempt: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            logger.LogWarning("Invalid folder patch request: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error patching folder {FolderName}", name);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete folder
    /// </summary>
    [HttpDelete("{name}")]
    public async Task<ActionResult> DeleteFolder(string name) {
        try {
            var userId = GetCurrentUserId();
            var decodedName = Uri.UnescapeDataString(name);
            var deleted = await folderService.DeleteFolderAsync(userId, decodedName);
            return deleted ? NoContent() : NotFound();
        } catch (UnauthorizedAccessException ex) {
            logger.LogWarning("Unauthorized access attempt: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            logger.LogWarning("Invalid folder deletion request: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            logger.LogError(ex, "Error deleting folder {FolderName}", name);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private int GetCurrentUserId() {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId)) {
            throw new UnauthorizedAccessException("Invalid user ID in token");
        }
        return userId;
    }
}
