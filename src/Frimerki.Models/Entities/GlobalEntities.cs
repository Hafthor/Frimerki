namespace Frimerki.Models.Entities;

/// <summary>
/// Registry of all domains in the system with their database names
/// </summary>
public class DomainRegistry {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Host administrators who can manage the entire email server
/// </summary>
public class HostAdmin {
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>
/// Server-wide configuration settings
/// </summary>
public class ServerConfiguration {
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public string? Description { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
