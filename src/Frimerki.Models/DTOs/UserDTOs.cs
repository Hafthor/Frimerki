using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class UserMinimalResponse {
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
}

public class UserResponse : UserMinimalResponse {
    public string? FullName { get; set; }
    public string Role { get; set; } = "";
    public bool CanReceive { get; set; }
    public bool CanLogin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public string DomainName { get; set; } = "";
    public UserStatsResponse Stats { get; set; } = new();
}

public class UserStatsResponse {
    public int MessageCount { get; set; }
    public long StorageUsed { get; set; }
    public string StorageUsedFormatted { get; set; } = "";
    public int FolderCount { get; set; }
    public DateTime? LastActivity { get; set; }
}

public class UserRequest {
    [Required]
    [StringLength(255, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Username can only contain letters, numbers, dots, hyphens, and underscores")]
    public string Username { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [StringLength(255)]
    public string? FullName { get; set; }

    [Required]
    [RegularExpression("^(User|DomainAdmin|HostAdmin)$", ErrorMessage = "Role must be User, DomainAdmin, or HostAdmin")]
    public string Role { get; set; } = "User";

    public bool CanReceive { get; set; } = true;

    public bool CanLogin { get; set; } = true;
}

public class UserUpdateRequest {
    [StringLength(255)]
    public string? FullName { get; set; }

    [RegularExpression("^(User|DomainAdmin|HostAdmin)$", ErrorMessage = "Role must be User, DomainAdmin, or HostAdmin")]
    public string? Role { get; set; }

    public bool? CanReceive { get; set; }

    public bool? CanLogin { get; set; }
}

public class UserPasswordUpdateRequest {
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string CurrentPassword { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";
}

// UserListResponse is now replaced by PaginatedInfo<UserResponse>

public class CreateUserRequest {
    [Required]
    [StringLength(255, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Username can only contain letters, numbers, dots, hyphens, and underscores")]
    public string Username { get; set; } = "";

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string DomainName { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [StringLength(255)]
    public string? FullName { get; set; }

    [Required]
    [RegularExpression("^(User|DomainAdmin|HostAdmin)$", ErrorMessage = "Role must be User, DomainAdmin, or HostAdmin")]
    public string Role { get; set; } = "User";

    public bool CanReceive { get; set; } = true;

    public bool CanLogin { get; set; } = true;
}
