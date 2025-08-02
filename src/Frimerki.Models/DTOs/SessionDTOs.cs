using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class LoginRequest {
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; } = false;
}

public class LoginResponse {
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public UserSessionInfo User { get; set; } = new();
    public string RefreshToken { get; set; } = "";
}

public class UserSessionInfo {
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string FullName { get; set; }
    public string Role { get; set; } = "";
    public bool CanReceive { get; set; }
    public bool CanLogin { get; set; }
    public string DomainName { get; set; } = "";
    public int DomainId { get; set; }
    public DateTime LastLogin { get; set; }
}

public class SessionResponse {
    public bool IsAuthenticated { get; set; }
    public UserSessionInfo User { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Token { get; set; }
}

public class RefreshTokenRequest {
    [Required]
    public string RefreshToken { get; set; } = "";
}

public class LogoutResponse {
    public string Message { get; set; } = "";
    public bool Success { get; set; }
}
