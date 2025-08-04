using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public record DomainResponse(string Name = "", string DatabaseName = "") {
    public bool IsActive { get; init; }
    public bool IsDedicated { get; init; }
    public string CatchAllUser { get; init; }
    public DateTime CreatedAt { get; init; }
    public int UserCount { get; init; }
    public long StorageUsed { get; init; }
    public DkimKeyInfo DkimKey { get; init; }
    public bool HasDkim { get; init; }
}

public record DomainRequest {
    [Required]
    [StringLength(255)]
    [RegularExpression(Constants.ValidDomainRegexPattern, ErrorMessage = "Invalid domain name format")]
    public string Name { get; init; } = "";

    [StringLength(255)]
    public string DatabaseName { get; init; }

    public bool CreateDatabase { get; init; }

    [EmailAddress]
    public string CatchAllUser { get; init; }
}

public record DomainUpdateRequest {
    public bool? IsActive { get; init; }

    [EmailAddress]
    public string CatchAllUser { get; init; }
}

public record DkimKeyInfo {
    public string Selector { get; init; } = "";
    public string PublicKey { get; init; } = "";
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record DkimKeyResponse {
    public string Selector { get; init; } = "";
    public string PublicKey { get; init; } = "";
    public string DnsRecord { get; init; } = "";
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; set; }
}

public class GenerateDkimKeyRequest {
    [StringLength(63)]
    [RegularExpression(Constants.ValidDkimRegexPattern, ErrorMessage = "Invalid DKIM selector format")]
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
