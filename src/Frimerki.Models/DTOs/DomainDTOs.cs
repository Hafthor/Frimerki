using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class DomainResponse {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? CatchAllUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UserCount { get; set; }
    public long StorageUsed { get; set; }
    public DkimKeyInfo? DkimKey { get; set; }
}

public class DomainRequest {
    [Required]
    [StringLength(255)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
        ErrorMessage = "Invalid domain name format")]
    public string Name { get; set; } = string.Empty;

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
    public string Selector { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DkimKeyResponse {
    public string Selector { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string DnsRecord { get; set; } = string.Empty;
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
    public List<DomainResponse> Domains { get; set; } = new();
    public int TotalCount { get; set; }
    public bool CanManageAll { get; set; }
}
