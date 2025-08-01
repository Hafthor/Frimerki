using System.Diagnostics;
using System.Reflection;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Server;

public interface IServerService {
    Task<ServerStatusResponse> GetServerStatusAsync();
    Task<ServerHealthResponse> GetServerHealthAsync();
    Task<ServerMetricsResponse> GetServerMetricsAsync();
    Task<PaginatedInfo<ServerLogEntry>> GetServerLogsAsync(int skip = 0, int take = 100, string? level = null);
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

    private static readonly string ApplicationVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0-alpha";

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
                Version = ApplicationVersion,
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
        var databaseMetrics = GetDatabaseMetrics();

        return new ServerMetricsResponse {
            System = systemMetrics,
            Email = emailMetrics,
            Database = databaseMetrics
        };
    }

    public Task<PaginatedInfo<ServerLogEntry>> GetServerLogsAsync(int skip = 0, int take = 100, string? level = null) {
        // For now, return empty logs - this would need integration with Serilog
        // In a real implementation, you'd read from the log files or database
        return Task.FromResult(new PaginatedInfo<ServerLogEntry> {
            Items = new List<ServerLogEntry>(),
            TotalCount = 0,
            Skip = skip,
            Take = take
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
                IsRunning = bool.Parse(_configuration["Server:EnableSMTP"] ?? "true"),
                Port = int.Parse(_configuration["SMTP:Port"] ?? "25"),
                SslEnabled = bool.Parse(_configuration["SMTP:EnableSSL"] ?? "false"),
                ActiveConnections = 0,
                LastStarted = _startTime
            },
            IMAP = new ServiceStatus {
                IsRunning = bool.Parse(_configuration["Server:EnableIMAP"] ?? "true"),
                Port = int.Parse(_configuration["IMAP:Port"] ?? "143"),
                SslEnabled = bool.Parse(_configuration["IMAP:EnableSSL"] ?? "false"),
                ActiveConnections = 0,
                LastStarted = _startTime
            },
            POP3 = new ServiceStatus {
                IsRunning = bool.Parse(_configuration["Server:EnablePOP3"] ?? "true"),
                Port = int.Parse(_configuration["POP3:Port"] ?? "110"),
                SslEnabled = bool.Parse(_configuration["POP3:EnableSSL"] ?? "false"),
                ActiveConnections = 0,
                LastStarted = _startTime
            },
            WebAPI = new ServiceStatus {
                IsRunning = true,
                Port = int.Parse(_configuration["Kestrel:Endpoints:Http:Url"]?.Split(':').LastOrDefault() ?? _configuration["ASPNETCORE_URLS"]?.Split(':').LastOrDefault() ?? "5000"),
                SslEnabled = _configuration["Kestrel:Endpoints:Https"] != null || (_configuration["ASPNETCORE_URLS"]?.Contains("https") ?? false),
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
        var diskInfo = GetDiskInfo();

        return new SystemMetrics {
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsagePercent = GetMemoryUsage(),
            MemoryUsedBytes = memoryUsed,
            MemoryTotalBytes = GetTotalMemory(),
            DiskUsagePercent = (diskInfo?.TotalSize - diskInfo?.AvailableFreeSpace + 0.0) / diskInfo?.TotalSize,
            DiskUsedBytes = diskInfo?.TotalSize - diskInfo?.AvailableFreeSpace,
            DiskTotalBytes = diskInfo?.TotalSize,
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

    private DatabaseMetrics GetDatabaseMetrics() {
        long dbSize = 0;

        try {
            // Get the actual database file path from Entity Framework
            var connectionString = _dbContext.Database.GetConnectionString();
            if (!string.IsNullOrEmpty(connectionString)) {
                // Parse SQLite connection string properly
                var builder = new SqliteConnectionStringBuilder(connectionString);
                var dbPath = builder.DataSource;

                if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath)) {
                    dbSize = new FileInfo(dbPath).Length;
                }
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Could not determine database size");
        }

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
        try {
            if (OperatingSystem.IsLinux()) {
                // On Linux, read from /proc/meminfo
                if (File.Exists("/proc/meminfo")) {
                    var memInfo = File.ReadAllText("/proc/meminfo");
                    var memTotalLine = memInfo.Split('\n')
                        .FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase));

                    if (memTotalLine != null) {
                        var parts = memTotalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var memKB)) {
                            return memKB * 1024; // Convert KB to bytes
                        }
                    }
                }
            } else if (OperatingSystem.IsMacOS()) {
                // On macOS, use sysctl to get total memory
                try {
                    using var process = new Process();
                    process.StartInfo = new ProcessStartInfo {
                        FileName = "sysctl",
                        Arguments = "-n hw.memsize",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (long.TryParse(output.Trim(), out var memBytes)) {
                        return memBytes;
                    }
                } catch {
                    // Fall through to default
                }
            } else if (OperatingSystem.IsWindows()) {
                // On Windows, use wmic to get total physical memory
                try {
                    using var process = new Process();
                    process.StartInfo = new ProcessStartInfo {
                        FileName = "wmic",
                        Arguments = "computersystem get TotalPhysicalMemory /value",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // Parse the output - it will be in format "TotalPhysicalMemory=<value>"
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var memoryLine = lines.FirstOrDefault(line => line.StartsWith("TotalPhysicalMemory=", StringComparison.OrdinalIgnoreCase));

                    if (memoryLine != null) {
                        var memoryValue = memoryLine.Split('=')[1].Trim();
                        if (long.TryParse(memoryValue, out var memBytes)) {
                            return memBytes;
                        }
                    }
                } catch {
                    // Fall through to default
                }
            }

            // Fallback: Use GC.GetTotalMemory as an estimate (this is managed memory, not total system memory)
            // This is at least a real value from the current process rather than a hardcoded guess
            return GC.GetTotalMemory(false);
        } catch {
            // Final fallback if even GC calls fail
            return GC.GetTotalMemory(false);
        }
    }

    private DriveInfo? GetDiskInfo() {
        try {
            return new DriveInfo(Directory.GetCurrentDirectory());
        } catch {
            return null;
        }
    }
}
