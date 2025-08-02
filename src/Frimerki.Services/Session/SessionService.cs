using System.Collections.Concurrent;
using System.Security.Claims;

using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Services.Authentication;
using Frimerki.Services.Common;
using Frimerki.Services.User;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Session;

public interface ISessionService {
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<bool> LogoutAsync(string userId);
    Task<SessionResponse> GetCurrentSessionAsync(ClaimsPrincipal user);
    Task<LoginResponse?> RefreshTokenAsync(string refreshToken);
    Task<bool> RevokeRefreshTokenAsync(string refreshToken);
}

public class SessionService : ISessionService {
    private readonly EmailDbContext _context;
    private readonly IUserService _userService;
    private readonly IJwtService _jwtService;
    private readonly INowProvider _nowProvider;
    private readonly ILogger<SessionService> _logger;

    // In-memory storage for refresh tokens (in production, use Redis or database)
    private static readonly ConcurrentDictionary<string, RefreshTokenInfo> _refreshTokens = new();

    public SessionService(
        EmailDbContext context,
        IUserService userService,
        IJwtService jwtService,
        INowProvider nowProvider,
        ILogger<SessionService> logger) {
        _context = context;
        _userService = userService;
        _jwtService = jwtService;
        _nowProvider = nowProvider;
        _logger = logger;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request) {
        _logger.LogInformation("Login attempt for user: {Email}", request.Email);

        try {
            // Authenticate user
            var user = await _userService.AuthenticateUserEntityAsync(request.Email, request.Password);
            if (user == null) {
                _logger.LogWarning("Login failed for user: {Email} - Invalid credentials", request.Email);
                return null;
            }

            if (!user.CanLogin) {
                _logger.LogWarning("Login failed for user: {Email} - Account disabled", request.Email);
                return null;
            }

            // Create session info
            var sessionInfo = new UserSessionInfo {
                Id = user.Id,
                Username = user.Username,
                Email = $"{user.Username}@{user.Domain.Name}",
                FullName = user.FullName,
                Role = user.Role,
                CanReceive = user.CanReceive,
                CanLogin = user.CanLogin,
                DomainName = user.Domain.Name,
                DomainId = user.DomainId,
                LastLogin = user.LastLogin ?? DateTime.MinValue
            };

            // Generate tokens
            var accessToken = _jwtService.GenerateAccessToken(sessionInfo);
            var refreshToken = _jwtService.GenerateRefreshToken();
            var expiresAt = _jwtService.GetTokenExpiration(request.RememberMe);

            // Store refresh token
            var now = _nowProvider.UtcNow;
            _refreshTokens[refreshToken] = new RefreshTokenInfo {
                UserId = user.Id,
                Email = $"{user.Username}@{user.Domain.Name}",
                CreatedAt = now,
                ExpiresAt = now.AddDays(30) // Refresh tokens expire in 30 days
            };

            _logger.LogInformation("Login successful for user: {Email}", request.Email);

            return new LoginResponse {
                Token = accessToken,
                ExpiresAt = expiresAt,
                User = sessionInfo,
                RefreshToken = refreshToken
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during login for user: {Email}", request.Email);
            return null;
        }
    }

    public async Task<bool> LogoutAsync(string userId) {
        _logger.LogInformation("Logout request for user ID: {UserId}", userId);

        try {
            // Remove all refresh tokens for this user
            var tokensToRemove = _refreshTokens
                .Where(kvp => kvp.Value.UserId.ToString() == userId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in tokensToRemove) {
                _refreshTokens.TryRemove(token, out _);
            }

            _logger.LogInformation("Logout successful for user ID: {UserId}", userId);
            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during logout for user ID: {UserId}", userId);
            return false;
        }
    }

    public async Task<SessionResponse> GetCurrentSessionAsync(ClaimsPrincipal user) {
        try {
            if (!user.Identity?.IsAuthenticated ?? false) {
                return new SessionResponse { IsAuthenticated = false };
            }

            var email = user.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(email)) {
                return new SessionResponse { IsAuthenticated = false };
            }

            // Get updated user information
            var userInfo = await _userService.GetUserEntityByEmailAsync(email);
            if (userInfo == null) {
                return new SessionResponse { IsAuthenticated = false };
            }

            var sessionInfo = new UserSessionInfo {
                Id = userInfo.Id,
                Username = userInfo.Username,
                Email = $"{userInfo.Username}@{userInfo.Domain.Name}",
                FullName = userInfo.FullName,
                Role = userInfo.Role,
                CanReceive = userInfo.CanReceive,
                CanLogin = userInfo.CanLogin,
                DomainName = userInfo.Domain.Name,
                DomainId = userInfo.DomainId,
                LastLogin = userInfo.LastLogin ?? DateTime.MinValue
            };

            // Generate a fresh token (auto-refresh functionality)
            var newToken = _jwtService.GenerateAccessToken(sessionInfo);
            var expiresAt = _jwtService.GetTokenExpiration();

            return new SessionResponse {
                IsAuthenticated = true,
                User = sessionInfo,
                ExpiresAt = expiresAt,
                Token = newToken
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting current session");
            return new SessionResponse { IsAuthenticated = false };
        }
    }

    public async Task<LoginResponse?> RefreshTokenAsync(string refreshToken) {
        _logger.LogInformation("Refresh token request");

        try {
            if (!_refreshTokens.TryGetValue(refreshToken, out var tokenInfo)) {
                _logger.LogWarning("Invalid refresh token provided");
                return null;
            }

            if (tokenInfo.ExpiresAt < _nowProvider.UtcNow) {
                _logger.LogWarning("Refresh token expired");
                _refreshTokens.TryRemove(refreshToken, out _);
                return null;
            }

            // Get current user information
            var user = await _userService.GetUserEntityByEmailAsync(tokenInfo.Email);
            if (user == null || !user.CanLogin) {
                _logger.LogWarning("User not found or disabled during token refresh: {Email}", tokenInfo.Email);
                _refreshTokens.TryRemove(refreshToken, out _);
                return null;
            }

            // Create new session info
            var sessionInfo = new UserSessionInfo {
                Id = user.Id,
                Username = user.Username,
                Email = $"{user.Username}@{user.Domain.Name}",
                FullName = user.FullName,
                Role = user.Role,
                CanReceive = user.CanReceive,
                CanLogin = user.CanLogin,
                DomainName = user.Domain.Name,
                DomainId = user.DomainId,
                LastLogin = user.LastLogin ?? DateTime.MinValue
            };

            // Generate new tokens
            var accessToken = _jwtService.GenerateAccessToken(sessionInfo);
            var newRefreshToken = _jwtService.GenerateRefreshToken();
            var expiresAt = _jwtService.GetTokenExpiration();

            // Replace old refresh token with new one
            _refreshTokens.TryRemove(refreshToken, out _);
            var now = _nowProvider.UtcNow;
            _refreshTokens[newRefreshToken] = new RefreshTokenInfo {
                UserId = user.Id,
                Email = $"{user.Username}@{user.Domain.Name}",
                CreatedAt = now,
                ExpiresAt = now.AddDays(30)
            };

            _logger.LogInformation("Token refresh successful for user: {Email}", $"{user.Username}@{user.Domain.Name}");

            return new LoginResponse {
                Token = accessToken,
                ExpiresAt = expiresAt,
                User = sessionInfo,
                RefreshToken = newRefreshToken
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during token refresh");
            return null;
        }
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken) {
        _logger.LogInformation("Refresh token revocation request");

        try {
            var removed = _refreshTokens.TryRemove(refreshToken, out _);
            _logger.LogInformation("Refresh token revocation result: {Removed}", removed);
            return removed;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during refresh token revocation");
            return false;
        }
    }

    private class RefreshTokenInfo {
        public int UserId { get; set; }
        public string Email { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
