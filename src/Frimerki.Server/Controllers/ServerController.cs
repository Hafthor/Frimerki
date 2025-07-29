using Frimerki.Models.DTOs;
using Frimerki.Services.Server;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/server")]
// TODO: Add [Authorize(Roles = "HostAdmin")] when authentication is implemented
public class ServerController : ControllerBase {
    private readonly IServerService _serverService;
    private readonly ILogger<ServerController> _logger;

    public ServerController(IServerService serverService, ILogger<ServerController> logger) {
        _serverService = serverService;
        _logger = logger;
    }

    /// <summary>
    /// Get server status and statistics (HostAdmin only)
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ServerStatusResponse>> GetServerStatus() {
        try {
            var status = await _serverService.GetServerStatusAsync();
            return Ok(status);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving server status");
            return StatusCode(500, new { error = "Failed to retrieve server status" });
        }
    }

    /// <summary>
    /// Health check endpoint (HostAdmin only)
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<ServerHealthResponse>> GetServerHealth() {
        try {
            var health = await _serverService.GetServerHealthAsync();

            // Return appropriate HTTP status based on health
            return health.Status switch {
                "Healthy" => Ok(health),
                "Warning" => StatusCode(200, health), // Still OK but with warnings
                "Critical" => StatusCode(503, health), // Service Unavailable
                _ => Ok(health)
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error performing health check");
            return StatusCode(500, new ServerHealthResponse {
                Status = "Critical",
                Message = "Health check failed due to internal error",
                Checks = new List<HealthCheck> {
                    new() {
                        Name = "System",
                        Status = "Critical",
                        Message = ex.Message,
                        ResponseTimeMs = 0
                    }
                }
            });
        }
    }

    /// <summary>
    /// Get performance metrics (HostAdmin only)
    /// </summary>
    [HttpGet("metrics")]
    public async Task<ActionResult<ServerMetricsResponse>> GetServerMetrics() {
        try {
            var metrics = await _serverService.GetServerMetricsAsync();
            return Ok(metrics);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving server metrics");
            return StatusCode(500, new { error = "Failed to retrieve server metrics" });
        }
    }

    /// <summary>
    /// Get server logs (HostAdmin only)
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<ServerLogsResponse>> GetServerLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? level = null) {
        try {
            if (page < 1) {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 1000) {
                pageSize = 100;
            }

            var logs = await _serverService.GetServerLogsAsync(page, pageSize, level);
            return Ok(logs);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving server logs");
            return StatusCode(500, new { error = "Failed to retrieve server logs" });
        }
    }

    /// <summary>
    /// Get server settings (HostAdmin only)
    /// </summary>
    [HttpGet("settings")]
    public async Task<ActionResult<ServerSettingsResponse>> GetServerSettings() {
        try {
            var settings = await _serverService.GetServerSettingsAsync();
            return Ok(settings);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving server settings");
            return StatusCode(500, new { error = "Failed to retrieve server settings" });
        }
    }

    /// <summary>
    /// Update server settings (HostAdmin only)
    /// </summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateServerSettings([FromBody] ServerSettingsRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            await _serverService.UpdateServerSettingsAsync(request);

            _logger.LogInformation("Server settings updated by admin");
            return Ok(new { message = "Server settings updated successfully" });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating server settings");
            return StatusCode(500, new { error = "Failed to update server settings" });
        }
    }

    /// <summary>
    /// Create server backup (HostAdmin only)
    /// </summary>
    [HttpPost("backup")]
    public async Task<ActionResult<BackupResponse>> CreateBackup([FromBody] BackupRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            var backup = await _serverService.CreateBackupAsync(request);

            _logger.LogInformation("Backup created: {BackupId}", backup.BackupId);
            return Ok(backup);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error creating backup");
            return StatusCode(500, new { error = "Failed to create backup" });
        }
    }

    /// <summary>
    /// Restore from backup (HostAdmin only)
    /// </summary>
    [HttpPost("restore")]
    public async Task<ActionResult<RestoreResponse>> RestoreFromBackup([FromBody] RestoreRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            var restore = await _serverService.RestoreFromBackupAsync(request);

            _logger.LogInformation("Restore completed from backup: {BackupId}", request.BackupId);
            return Ok(restore);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error restoring from backup");
            return StatusCode(500, new { error = "Failed to restore from backup" });
        }
    }

    /// <summary>
    /// Download backup file (HostAdmin only)
    /// </summary>
    [HttpGet("backup/{backupId}/download")]
    public async Task<IActionResult> DownloadBackup(string backupId) {
        try {
            // In a real implementation, validate backupId and stream the file
            _logger.LogInformation("Backup download requested: {BackupId}", backupId);

            // For now, return not found since we don't have actual backup files
            return NotFound(new { error = "Backup file not found" });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error downloading backup {BackupId}", backupId);
            return StatusCode(500, new { error = "Failed to download backup" });
        }
    }

    /// <summary>
    /// List available backups (HostAdmin only)
    /// </summary>
    [HttpGet("backups")]
    public async Task<ActionResult<List<BackupResponse>>> ListBackups() {
        try {
            // In a real implementation, list actual backup files
            List<BackupResponse> backups = [];

            return Ok(backups);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error listing backups");
            return StatusCode(500, new { error = "Failed to list backups" });
        }
    }

    /// <summary>
    /// Delete a backup (HostAdmin only)
    /// </summary>
    [HttpDelete("backup/{backupId}")]
    public async Task<IActionResult> DeleteBackup(string backupId) {
        try {
            // In a real implementation, delete the backup file
            _logger.LogInformation("Backup deletion requested: {BackupId}", backupId);

            return Ok(new { message = "Backup deleted successfully" });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error deleting backup {BackupId}", backupId);
            return StatusCode(500, new { error = "Failed to delete backup" });
        }
    }

    /// <summary>
    /// Restart server services (HostAdmin only)
    /// </summary>
    [HttpPost("restart")]
    public async Task<IActionResult> RestartServer([FromQuery] string? service = null) {
        try {
            _logger.LogWarning("Server restart requested by admin. Service: {Service}", service ?? "all");

            // In a real implementation, restart the specified service or all services
            return Ok(new {
                message = service != null
                    ? $"Service {service} restart initiated"
                    : "Server restart initiated",
                warning = "This operation will temporarily interrupt service"
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error restarting server/service");
            return StatusCode(500, new { error = "Failed to restart server" });
        }
    }

    /// <summary>
    /// Get system information (HostAdmin only)
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetSystemInfo() {
        try {
            var info = new {
                ServerName = Environment.MachineName,
                OperatingSystem = Environment.OSVersion.ToString(),
                Framework = Environment.Version.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                WorkingDirectory = Environment.CurrentDirectory,
                ApplicationVersion = "1.0.0-alpha",
                StartTime = System.Diagnostics.Process.GetCurrentProcess().StartTime,
                Uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime
            };

            return Ok(info);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving system information");
            return StatusCode(500, new { error = "Failed to retrieve system information" });
        }
    }
}
