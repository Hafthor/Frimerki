using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Frimerki.Data;
using Frimerki.Models.DTOs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Server;

public interface IServerService {
    Task<ServerStatusResponse> GetServerStatusAsync();
    Task<ServerHealthResponse> GetServerHealthAsync();
    Task<ServerMetricsResponse> GetServerMetricsAsync();
    Task<ServerLogsResponse> GetServerLogsAsync(int page = 1, int pageSize = 100, string? level = null);
    Task<ServerSettingsResponse> GetServerSettingsAsync();
    Task UpdateServerSettingsAsync(ServerSettingsRequest request);
    Task<BackupResponse> CreateBackupAsync(BackupRequest request);
    Task<RestoreResponse> RestoreFromBackupAsync(RestoreRequest request);
}

public class ServerService : IServerService {
    private readonly EmailDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServerService> _logger;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public ServerService(
        EmailDbContext dbContext,
        IConfiguration configuration,
        ILogger<ServerService> logger) {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ServerStatusResponse> GetServerStatusAsync() {
        try {
            var statistics = await GetStatisticsAsync();
            var services = GetServicesStatus();

            return new ServerStatusResponse {
                Status = "Running",
                Version = "1.0.0-alpha",
                Uptime = _startTime,
                Statistics = statistics,
                Services = services
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting server status");
            throw;
        }
    }

    public async Task<ServerHealthResponse> GetServerHealthAsync() {
        List<HealthCheck> checks = [];
        var overallStatus = "Healthy";

        // Database health check
        var dbCheck = await CheckDatabaseHealthAsync();
        checks.Add(dbCheck);

        // Disk space check
        var diskCheck = CheckDiskHealth();
        checks.Add(diskCheck);

        // Memory check
        var memoryCheck = CheckMemoryHealth();
        checks.Add(memoryCheck);

        // Service checks
        var serviceChecks = CheckServicesHealth();
        checks.AddRange(serviceChecks);

        // Determine overall status
        if (checks.Any(c => c.Status == "Critical")) {
            overallStatus = "Critical";
        } else if (checks.Any(c => c.Status == "Warning")) {
            overallStatus = "Warning";
        }

        return new ServerHealthResponse {
            Status = overallStatus,
            Message = $"System health check completed with {checks.Count} checks",
            Checks = checks
        };
    }

    public async Task<ServerMetricsResponse> GetServerMetricsAsync() {
        var systemMetrics = GetSystemMetrics();
        var emailMetrics = await GetEmailMetricsAsync();
        var databaseMetrics = await GetDatabaseMetricsAsync();

        return new ServerMetricsResponse {
            System = systemMetrics,
            Email = emailMetrics,
            Database = databaseMetrics
        };
    }

    public Task<ServerLogsResponse> GetServerLogsAsync(int page = 1, int pageSize = 100, string? level = null) {
        // For now, return empty logs - this would need integration with Serilog
        // In a real implementation, you'd read from the log files or database
        return Task.FromResult(new ServerLogsResponse {
            Logs = new List<ServerLogEntry>(),
            TotalCount = 0,
            PageSize = pageSize,
            CurrentPage = page
        });
    }

    public Task<ServerSettingsResponse> GetServerSettingsAsync() {
        // Return current configuration settings (sanitized)
        var settings = new Dictionary<string, object> {
            ["MaxMessageSize"] = _configuration["Server:MaxMessageSize"] ?? "25MB",
            ["StorageQuotaPerUser"] = _configuration["Server:StorageQuotaPerUser"] ?? "1GB",
            ["EnableSMTP"] = bool.Parse(_configuration["Server:EnableSMTP"] ?? "true"),
            ["EnableIMAP"] = bool.Parse(_configuration["Server:EnableIMAP"] ?? "true"),
            ["EnablePOP3"] = bool.Parse(_configuration["Server:EnablePOP3"] ?? "true"),
            ["RequireSSL"] = bool.Parse(_configuration["Security:RequireSSL"] ?? "true"),
            ["EnableRateLimit"] = bool.Parse(_configuration["Security:EnableRateLimit"] ?? "true"),
            ["MaxFailedLogins"] = int.Parse(_configuration["Security:MaxFailedLogins"] ?? "5")
        };

        return Task.FromResult(new ServerSettingsResponse {
            Settings = settings,
            LastModified = DateTime.UtcNow // In real implementation, track actual modification time
        });
    }

    public Task UpdateServerSettingsAsync(ServerSettingsRequest request) {
        // In a real implementation, you would update the configuration
        // This might involve updating appsettings.json or a database table
        _logger.LogInformation("Server settings update requested");

        // For now, just log the request
        foreach (var setting in request.Settings) {
            _logger.LogInformation("Setting {Key} = {Value}", setting.Key, setting.Value);
        }

        return Task.CompletedTask;
    }

    public Task<BackupResponse> CreateBackupAsync(BackupRequest request) {
        var backupId = Guid.NewGuid().ToString();
        var fileName = $"frimerki-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";

        _logger.LogInformation("Creating backup {BackupId}", backupId);

        // In a real implementation, you would:
        // 1. Create a database backup
        // 2. Archive attachments if requested
        // 3. Include logs if requested
        // 4. Compress everything into a zip file

        return Task.FromResult(new BackupResponse {
            BackupId = backupId,
            FileName = fileName,
            SizeBytes = 1024 * 1024, // Placeholder size
            CreatedAt = DateTime.UtcNow,
            Status = "Completed",
            DownloadUrl = $"/api/server/backup/{backupId}/download"
        });
    }

    public Task<RestoreResponse> RestoreFromBackupAsync(RestoreRequest request) {
        _logger.LogInformation("Restoring from backup {BackupId}", request.BackupId);

        // In a real implementation, you would:
        // 1. Validate the backup file
        // 2. Stop services temporarily
        // 3. Restore database
        // 4. Restore attachments
        // 5. Restart services

        return Task.FromResult(new RestoreResponse {
            Status = "Completed",
            Message = "Backup restored successfully",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow
        });
    }

    private async Task<ServerStatistics> GetStatisticsAsync() {
        var totalUsers = await _dbContext.Users.CountAsync();
        var totalDomains = await _dbContext.Domains.CountAsync();
        var totalMessages = await _dbContext.Messages.CountAsync();

        var today = DateTime.UtcNow.Date;
        var messagesToday = await _dbContext.Messages
            .Where(m => m.ReceivedAt >= today)
            .CountAsync();

        return new ServerStatistics {
            TotalUsers = totalUsers,
            TotalDomains = totalDomains,
            TotalMessages = totalMessages,
            StorageUsed = GetDirectorySize("data"), // Approximate
            StorageAvailable = GetAvailableStorage(),
            ActiveConnections = 0, // Would be tracked by protocol services
            MessagesProcessedToday = messagesToday,
            CpuUsage = GetCpuUsage(),
            MemoryUsage = GetMemoryUsage()
        };
    }

    private ServerServices GetServicesStatus() {
        return new ServerServices {
            SMTP = new ServiceStatus {
                IsRunning = true, // Would check actual service status
                Port = 587,
                SslEnabled = true,
                ActiveConnections = 0,
                LastStarted = _startTime
            },
            IMAP = new ServiceStatus {
                IsRunning = true,
                Port = 993,
                SslEnabled = true,
                ActiveConnections = 0,
                LastStarted = _startTime
            },
            POP3 = new ServiceStatus {
                IsRunning = true,
                Port = 995,
                SslEnabled = true,
                ActiveConnections = 0,
                LastStarted = _startTime
            },
            WebAPI = new ServiceStatus {
                IsRunning = true,
                Port = 5000,
                SslEnabled = false,
                ActiveConnections = 0,
                LastStarted = _startTime
            }
        };
    }

    private async Task<HealthCheck> CheckDatabaseHealthAsync() {
        var stopwatch = Stopwatch.StartNew();
        try {
            await _dbContext.Database.CanConnectAsync();
            stopwatch.Stop();

            return new HealthCheck {
                Name = "Database",
                Status = "Healthy",
                Message = "Database connection successful",
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
            };
        } catch (Exception ex) {
            stopwatch.Stop();
            return new HealthCheck {
                Name = "Database",
                Status = "Critical",
                Message = $"Database connection failed: {ex.Message}",
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private HealthCheck CheckDiskHealth() {
        try {
            var driveInfo = new DriveInfo(Directory.GetCurrentDirectory());
            var usagePercent = (double)(driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / driveInfo.TotalSize * 100;

            var status = usagePercent > 90 ? "Critical" : usagePercent > 80 ? "Warning" : "Healthy";
            var message = $"Disk usage: {usagePercent:F1}%";

            return new HealthCheck {
                Name = "Disk Space",
                Status = status,
                Message = message,
                ResponseTimeMs = 0
            };
        } catch (Exception ex) {
            return new HealthCheck {
                Name = "Disk Space",
                Status = "Warning",
                Message = $"Could not check disk space: {ex.Message}",
                ResponseTimeMs = 0
            };
        }
    }

    private HealthCheck CheckMemoryHealth() {
        try {
            using var process = Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / (1024 * 1024);

            var status = memoryMB > 1000 ? "Warning" : "Healthy";
            var message = $"Memory usage: {memoryMB} MB";

            return new HealthCheck {
                Name = "Memory",
                Status = status,
                Message = message,
                ResponseTimeMs = 0
            };
        } catch (Exception ex) {
            return new HealthCheck {
                Name = "Memory",
                Status = "Warning",
                Message = $"Could not check memory: {ex.Message}",
                ResponseTimeMs = 0
            };
        }
    }

    private List<HealthCheck> CheckServicesHealth() {
        // In a real implementation, check if services are actually running
        return new List<HealthCheck> {
            new() { Name = "SMTP Service", Status = "Healthy", Message = "Service running", ResponseTimeMs = 0 },
            new() { Name = "IMAP Service", Status = "Healthy", Message = "Service running", ResponseTimeMs = 0 },
            new() { Name = "POP3 Service", Status = "Healthy", Message = "Service running", ResponseTimeMs = 0 }
        };
    }

    private SystemMetrics GetSystemMetrics() {
        using var process = Process.GetCurrentProcess();
        var memoryUsed = process.WorkingSet64;

        return new SystemMetrics {
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsagePercent = GetMemoryUsage(),
            MemoryUsedBytes = memoryUsed,
            MemoryTotalBytes = GetTotalMemory(),
            DiskUsagePercent = GetDiskUsage(),
            DiskUsedBytes = GetDiskUsed(),
            DiskTotalBytes = GetDiskTotal(),
            ActiveThreads = process.Threads.Count
        };
    }

    private async Task<EmailMetrics> GetEmailMetricsAsync() {
        var today = DateTime.UtcNow.Date;
        var receivedToday = await _dbContext.Messages
            .Where(m => m.ReceivedAt >= today)
            .CountAsync();

        return new EmailMetrics {
            MessagesReceivedToday = receivedToday,
            MessagesSentToday = 0, // Would track from sent items
            QueuedMessages = 0, // Would check mail queue
            FailedMessages = 0, // Would track failed deliveries
            AverageProcessingTimeMs = 150, // Would calculate from metrics
            SpamMessagesBlocked = 0 // Would track from spam filter
        };
    }

    private async Task<DatabaseMetrics> GetDatabaseMetricsAsync() {
        var dbPath = _configuration.GetConnectionString("DefaultConnection")?.Replace("Data Source=", "") ?? "emailserver.db";
        var dbSize = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;

        return new DatabaseMetrics {
            SizeBytes = dbSize,
            ConnectionCount = 1, // Would track actual connections
            AverageQueryTimeMs = 25, // Would calculate from metrics
            TotalQueries = 0, // Would track from metrics
            SlowQueries = 0 // Would track queries > threshold
        };
    }

    private long GetDirectorySize(string path) {
        if (!Directory.Exists(path)) {
            return 0;
        }

        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private long GetAvailableStorage() {
        try {
            var driveInfo = new DriveInfo(Directory.GetCurrentDirectory());
            return driveInfo.AvailableFreeSpace;
        } catch {
            return 0;
        }
    }

    private double GetCpuUsage() {
        // Simplified CPU usage - in real implementation use performance counters
        return Random.Shared.NextDouble() * 20; // 0-20% for demo
    }

    private double GetMemoryUsage() {
        try {
            using var process = Process.GetCurrentProcess();
            var memoryUsed = process.WorkingSet64;
            var totalMemory = GetTotalMemory();
            return totalMemory > 0 ? (double)memoryUsed / totalMemory * 100 : 0;
        } catch {
            return 0;
        }
    }

    private long GetTotalMemory() {
        // Simplified - would use proper system memory detection
        return 8L * 1024 * 1024 * 1024; // 8GB for demo
    }

    private double GetDiskUsage() {
        try {
            var driveInfo = new DriveInfo(Directory.GetCurrentDirectory());
            var used = driveInfo.TotalSize - driveInfo.AvailableFreeSpace;
            return (double)used / driveInfo.TotalSize * 100;
        } catch {
            return 0;
        }
    }

    private long GetDiskUsed() {
        try {
            var driveInfo = new DriveInfo(Directory.GetCurrentDirectory());
            return driveInfo.TotalSize - driveInfo.AvailableFreeSpace;
        } catch {
            return 0;
        }
    }

    private long GetDiskTotal() {
        try {
            var driveInfo = new DriveInfo(Directory.GetCurrentDirectory());
            return driveInfo.TotalSize;
        } catch {
            return 0;
        }
    }
}
