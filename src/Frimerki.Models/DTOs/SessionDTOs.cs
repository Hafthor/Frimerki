using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class LoginRequest {
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = false;
}

public class LoginResponse {
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserSessionInfo User { get; set; } = new();
    public string RefreshToken { get; set; } = string.Empty;
}

public class UserSessionInfo {
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool CanReceive { get; set; }
    public bool CanLogin { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public int DomainId { get; set; }
    public DateTime? LastLogin { get; set; }
}

public class SessionResponse {
    public bool IsAuthenticated { get; set; }
    public UserSessionInfo? User { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Token { get; set; }
}

public class RefreshTokenRequest {
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class LogoutResponse {
    public string Message { get; set; } = string.Empty;
    public bool Success { get; set; }
}
