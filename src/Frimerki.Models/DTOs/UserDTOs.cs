using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public record UserMinimalResponse {
    public string Email { get; init; } = "";
    public string Username { get; init; } = "";
}

public record UserResponse : UserMinimalResponse {
    public string FullName { get; init; }
    public string Role { get; init; } = "";
    public bool CanReceive { get; init; }
    public bool CanLogin { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastLogin { get; init; }
    public string DomainName { get; init; } = "";
    public UserStatsResponse Stats { get; init; } = new();
}

public record UserStatsResponse {
    public int MessageCount { get; init; }
    public long StorageUsed { get; init; }
    public string StorageUsedFormatted { get; init; } = "";
    public int FolderCount { get; init; }
    public DateTime LastActivity { get; init; }
}

public record UserRequest {
    [Required]
    [StringLength(255, MinimumLength = 1)]
    [RegularExpression(Constants.ValidUsernameRegexPattern,
        ErrorMessage = "Username can only contain letters, numbers, dots, hyphens, and underscores")]
    public string Username { get; init; } = "";

    [Required]
    [EmailAddress]
    public string Email { get; init; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = "";

    [StringLength(255)]
    public string FullName { get; init; }

    [Required]
    [RegularExpression(Constants.ValidUserRoleRegexPattern, ErrorMessage = "Role must be User, DomainAdmin, or HostAdmin")]
    public string Role { get; init; } = "User";

    public bool CanReceive { get; init; } = true;

    public bool CanLogin { get; init; } = true;
}

public record UserUpdateRequest {
    [StringLength(255)]
    public string FullName { get; init; }

    [RegularExpression("^(User|DomainAdmin|HostAdmin)$", ErrorMessage = "Role must be User, DomainAdmin, or HostAdmin")]
    public string Role { get; init; }

    public bool? CanReceive { get; init; }

    public bool? CanLogin { get; init; }
}

public record UserPasswordUpdateRequest {
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string CurrentPassword { get; init; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; init; } = "";
}

// UserListResponse is now replaced by PaginatedInfo<UserResponse>

public record CreateUserRequest {
    [Required]
    [StringLength(255, MinimumLength = 1)]
    [RegularExpression(Constants.ValidUsernameRegexPattern,
        ErrorMessage = "Username can only contain letters, numbers, dots, hyphens, and underscores")]
    public string Username { get; init; } = "";

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string DomainName { get; init; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = "";

    [StringLength(255)]
    public string FullName { get; init; }

    [Required]
    [RegularExpression(Constants.ValidUserRoleRegexPattern, ErrorMessage = "Role must be User, DomainAdmin, or HostAdmin")]
    public string Role { get; init; } = "User";

    public bool CanReceive { get; init; } = true;

    public bool CanLogin { get; init; } = true;
}
