namespace Frimerki.Models.Entities;

/// <summary>
/// Registry of all domains in the system with their database names
/// </summary>
public record DomainRegistry {
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string DatabaseName { get; init; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Host administrators who can manage the entire email server
/// </summary>
public record HostAdmin {
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string PasswordHash { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public DateTime CreatedAt { get; init; }
    public DateTime LastLoginAt { get; init; }
    public string Email { get; init; }
    public string DisplayName { get; init; }
}

/// <summary>
/// Server-wide configuration settings
/// </summary>
public record ServerConfiguration {
    public int Id { get; init; }
    public string Key { get; init; } = "";
    public string Value { get; init; }
    public string Description { get; init; }
    public DateTime ModifiedAt { get; init; }
    public string ModifiedBy { get; init; }
}
