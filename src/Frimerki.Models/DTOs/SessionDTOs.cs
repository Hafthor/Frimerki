using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public record LoginRequest {
    [Required]
    [EmailAddress]
    public string Email { get; init; } = "";

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Password { get; init; } = "";

    public bool RememberMe { get; init; }
}

public record LoginResponse {
    public string Token { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public UserSessionInfo User { get; init; } = new();
    public string RefreshToken { get; init; } = "";
}

public record UserSessionInfo {
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string Email { get; init; } = "";
    public string FullName { get; init; }
    public string Role { get; init; } = "";
    public bool CanReceive { get; init; }
    public bool CanLogin { get; init; }
    public string DomainName { get; init; } = "";
    public int DomainId { get; init; }
    public DateTime LastLogin { get; init; }
}

public record SessionResponse {
    public bool IsAuthenticated { get; init; }
    public UserSessionInfo User { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string Token { get; init; }
}

public record RefreshTokenRequest {
    [Required]
    public string RefreshToken { get; init; } = "";
}

public record LogoutResponse {
    public string Message { get; init; } = "";
    public bool Success { get; init; }
}
