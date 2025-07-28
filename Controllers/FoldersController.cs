using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Frimerki.DTOs;
using Frimerki.Services;

namespace Frimerki.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly FolderService _folderService;

    public FoldersController(FolderService folderService)
    {
        _folderService = folderService;
    }

    [HttpGet]
    public async Task<ActionResult<List<FolderListResponse>>> GetFolders()
    {
        var userId = GetCurrentUserId();
        var folders = await _folderService.GetFoldersAsync(userId);
        return Ok(folders);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<FolderResponse>> GetFolder(string name)
    {
        var userId = GetCurrentUserId();
        var decodedName = Uri.UnescapeDataString(name);
        var folder = await _folderService.GetFolderAsync(userId, decodedName);
        return folder == null ? NotFound() : Ok(folder);
    }

    [HttpPost]
    public async Task<ActionResult<FolderResponse>> CreateFolder(FolderRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var folder = await _folderService.CreateFolderAsync(userId, request);
            return CreatedAtAction(nameof(GetFolder), new { name = Uri.EscapeDataString(folder.Name) }, folder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{name}")]
    public async Task<ActionResult<FolderResponse>> UpdateFolder(string name, FolderUpdateRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var decodedName = Uri.UnescapeDataString(name);
            var folder = await _folderService.UpdateFolderAsync(userId, decodedName, request);
            return folder == null ? NotFound() : Ok(folder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{name}")]
    public async Task<ActionResult> DeleteFolder(string name)
    {
        try
        {
            var userId = GetCurrentUserId();
            var decodedName = Uri.UnescapeDataString(name);
            var deleted = await _folderService.DeleteFolderAsync(userId, decodedName);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user ID in token");
        }
        return userId;
    }
}
