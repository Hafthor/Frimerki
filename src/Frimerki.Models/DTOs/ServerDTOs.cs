using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class ServerStatusResponse {
    public string Status { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime Uptime { get; set; }
    public ServerStatistics Statistics { get; set; } = new();
    public ServerServices Services { get; set; } = new();
}

public class ServerStatistics {
    public int TotalUsers { get; set; }
    public int TotalDomains { get; set; }
    public int TotalMessages { get; set; }
    public long StorageUsed { get; set; }
    public long StorageAvailable { get; set; }
    public int ActiveConnections { get; set; }
    public int MessagesProcessedToday { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
}

public class ServerServices {
    public ServiceStatus SMTP { get; set; } = new();
    public ServiceStatus IMAP { get; set; } = new();
    public ServiceStatus POP3 { get; set; } = new();
    public ServiceStatus WebAPI { get; set; } = new();
}

public class ServiceStatus {
    public bool IsRunning { get; set; }
    public int Port { get; set; }
    public bool SslEnabled { get; set; }
    public int ActiveConnections { get; set; }
    public DateTime LastStarted { get; set; }
}

public class ServerHealthResponse {
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public List<HealthCheck> Checks { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class HealthCheck {
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; }
    public long ResponseTimeMs { get; set; }
}

public class ServerMetricsResponse {
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public SystemMetrics System { get; set; } = new();
    public EmailMetrics Email { get; set; } = new();
    public DatabaseMetrics Database { get; set; } = new();
}

public class SystemMetrics {
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double? DiskUsagePercent { get; set; }
    public long? DiskUsedBytes { get; set; }
    public long? DiskTotalBytes { get; set; }
    public int ActiveThreads { get; set; }
}

public class EmailMetrics {
    public int MessagesReceivedToday { get; set; }
    public int MessagesSentToday { get; set; }
    public int QueuedMessages { get; set; }
    public int FailedMessages { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public int SpamMessagesBlocked { get; set; }
}

public class DatabaseMetrics {
    public long SizeBytes { get; set; }
    public int ConnectionCount { get; set; }
    public double AverageQueryTimeMs { get; set; }
    public int TotalQueries { get; set; }
    public int SlowQueries { get; set; }
}

public class ServerLogEntry {
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Logger { get; set; } = "";
    public string Message { get; set; } = "";
    public string Exception { get; set; }
    public Dictionary<string, object> Properties { get; set; }
}

public class ServerSettingsRequest {
    [Required]
    public Dictionary<string, object> Settings { get; set; } = new();
}

public class ServerSettingsResponse {
    public Dictionary<string, object> Settings { get; set; } = new();
    public DateTime LastModified { get; set; }
}

public class BackupRequest {
    public bool IncludeAttachments { get; set; } = true;
    public bool IncludeLogs { get; set; } = false;
    public string Description { get; set; }
}

public class BackupResponse {
    public string BackupId { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "";
    public string DownloadUrl { get; set; }
}

public class RestoreRequest {
    [Required]
    public string BackupId { get; set; } = "";
    public bool RestoreSettings { get; set; } = true;
    public bool RestoreUsers { get; set; } = true;
    public bool RestoreMessages { get; set; } = true;
    public bool RestoreAttachments { get; set; } = true;
}

public class RestoreResponse {
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
}
