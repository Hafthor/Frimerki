using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class DomainResponse {
    public string Name { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsDedicated { get; set; }
    public string? CatchAllUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UserCount { get; set; }
    public long StorageUsed { get; set; }
    public DkimKeyInfo? DkimKey { get; set; }
    public bool HasDkim { get; set; }
}

public class DomainRequest {
    [Required]
    [StringLength(255)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
        ErrorMessage = "Invalid domain name format")]
    public string Name { get; set; } = "";

    [StringLength(255)]
    public string? DatabaseName { get; set; }

    public bool CreateDatabase { get; set; } = false;

    public bool IsActive { get; set; } = true;

    [EmailAddress]
    public string? CatchAllUser { get; set; }
}

public class DomainUpdateRequest {
    [StringLength(255)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
        ErrorMessage = "Invalid domain name format")]
    public string? Name { get; set; }

    public bool? IsActive { get; set; }

    [EmailAddress]
    public string? CatchAllUser { get; set; }
}

public class DkimKeyInfo {
    public string Selector { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DkimKeyResponse {
    public string Selector { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string DnsRecord { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GenerateDkimKeyRequest {
    [StringLength(63)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$",
        ErrorMessage = "Invalid DKIM selector format")]
    public string Selector { get; set; } = "default";

    [Range(1024, 4096)]
    public int KeySize { get; set; } = 2048;
}

public class DomainListResponse {
    public List<DomainResponse> Domains { get; set; } = [];
    public int TotalCount { get; set; }
    public bool CanManageAll { get; set; }
}

public class CreateDomainResponse {
    public string Name { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsDedicated { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool DatabaseCreated { get; set; }
    public InitialSetupInfo InitialSetup { get; set; } = new();
}

public class InitialSetupInfo {
    public bool AdminUserCreated { get; set; }
    public bool DefaultFoldersCreated { get; set; }
    public bool DkimKeysGenerated { get; set; }
}

public class DatabaseListResponse {
    public List<DatabaseInfo> Databases { get; set; } = [];
    public long TotalSize { get; set; }
    public int TotalDomains { get; set; }
    public int TotalUsers { get; set; }
    public int TotalMessages { get; set; }
}

public class DatabaseInfo {
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public List<string> Domains { get; set; } = [];
    public int TotalUsers { get; set; }
    public int TotalMessages { get; set; }
    public bool IsDedicated { get; set; }
    public DateTime CreatedAt { get; set; }
}
