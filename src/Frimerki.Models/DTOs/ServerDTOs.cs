using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public record ServerStatusResponse {
    public string Status { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime Uptime { get; init; }
    public ServerStatistics Statistics { get; init; } = new();
    public ServerServices Services { get; init; } = new();
}

public record ServerStatistics {
    public int TotalUsers { get; init; }
    public int TotalDomains { get; init; }
    public int TotalMessages { get; init; }
    public long StorageUsed { get; init; }
    public long StorageAvailable { get; init; }
    public int ActiveConnections { get; init; }
    public int MessagesProcessedToday { get; init; }
    public double CpuUsage { get; init; }
    public double MemoryUsage { get; init; }
}

public record ServerServices {
    public ServiceStatus Smtp { get; init; } = new();
    public ServiceStatus Imap { get; init; } = new();
    public ServiceStatus Pop3 { get; init; } = new();
    public ServiceStatus WebApi { get; init; } = new();
}

public record ServiceStatus {
    public bool IsRunning { get; init; }
    public int Port { get; init; }
    public bool SslEnabled { get; init; }
    public int ActiveConnections { get; init; }
    public DateTime LastStarted { get; init; }
}

public record ServerHealthResponse {
    public string Status { get; init; } = "";
    public string Message { get; init; } = "";
    public List<HealthCheck> Checks { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public record HealthCheck {
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public string Message { get; init; }
    public long ResponseTimeMs { get; init; }
}

public record ServerMetricsResponse {
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public SystemMetrics System { get; init; } = new();
    public EmailMetrics Email { get; init; } = new();
    public DatabaseMetrics Database { get; init; } = new();
}

public record SystemMetrics {
    public double CpuUsagePercent { get; init; }
    public double MemoryUsagePercent { get; init; }
    public long MemoryUsedBytes { get; init; }
    public long MemoryTotalBytes { get; init; }
    public double? DiskUsagePercent { get; init; }
    public long? DiskUsedBytes { get; init; }
    public long? DiskTotalBytes { get; init; }
    public int ActiveThreads { get; init; }
}

public record EmailMetrics {
    public int MessagesReceivedToday { get; init; }
    public int MessagesSentToday { get; init; }
    public int QueuedMessages { get; init; }
    public int FailedMessages { get; init; }
    public double AverageProcessingTimeMs { get; init; }
    public int SpamMessagesBlocked { get; init; }
}

public record DatabaseMetrics {
    public long SizeBytes { get; init; }
    public int ConnectionCount { get; init; }
    public double AverageQueryTimeMs { get; init; }
    public int TotalQueries { get; init; }
    public int SlowQueries { get; init; }
}

public record ServerLogEntry {
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = "";
    public string Logger { get; init; } = "";
    public string Message { get; init; } = "";
    public string Exception { get; init; }
    public Dictionary<string, object> Properties { get; init; }
}

public record ServerSettingsRequest {
    [Required]
    public Dictionary<string, object> Settings { get; init; } = new();
}

public record ServerSettingsResponse {
    public Dictionary<string, object> Settings { get; init; } = new();
    public DateTime LastModified { get; init; }
}

public record BackupRequest {
    public bool IncludeAttachments { get; init; } = true;
    public bool IncludeLogs { get; init; } = false;
    public string Description { get; init; }
}

public record BackupResponse {
    public string BackupId { get; init; } = "";
    public string FileName { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Status { get; init; } = "";
    public string DownloadUrl { get; init; }
}

public record RestoreRequest {
    [Required]
    public string BackupId { get; init; } = "";
    public bool RestoreSettings { get; init; } = true;
    public bool RestoreUsers { get; init; } = true;
    public bool RestoreMessages { get; init; } = true;
    public bool RestoreAttachments { get; init; } = true;
}

public record RestoreResponse {
    public string Status { get; init; } = "";
    public string Message { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
}
