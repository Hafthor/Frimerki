using System.Collections.Concurrent;
using System.Security.Claims;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Services.Authentication;
using Frimerki.Services.Common;
using Frimerki.Services.User;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Session;

public interface ISessionService {
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<bool> LogoutAsync(string userId);
    Task<SessionResponse> GetCurrentSessionAsync(ClaimsPrincipal user);
    Task<LoginResponse> RefreshTokenAsync(string refreshToken);
    Task<bool> RevokeRefreshTokenAsync(string refreshToken);
}

public class SessionService(
    EmailDbContext context,
    IUserService userService,
    IJwtService jwtService,
    INowProvider nowProvider,
    ILogger<SessionService> logger)
    : ISessionService {
    // In-memory storage for refresh tokens (in production, use Redis or database)
    private static readonly ConcurrentDictionary<string, RefreshTokenInfo> RefreshTokens = new();

    public async Task<LoginResponse> LoginAsync(LoginRequest request) {
        logger.LogInformation("Login attempt for user: {Email}", request.Email);

        try {
            // Authenticate user
            var user = await userService.AuthenticateUserEntityAsync(request.Email, request.Password);
            if (user == null) {
                logger.LogWarning("Login failed for user: {Email} - Invalid credentials", request.Email);
                return null;
            }

            if (!user.CanLogin) {
                logger.LogWarning("Login failed for user: {Email} - Account disabled", request.Email);
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
            var accessToken = jwtService.GenerateAccessToken(sessionInfo);
            var refreshToken = jwtService.GenerateRefreshToken();
            var expiresAt = jwtService.GetTokenExpiration(request.RememberMe);

            // Store refresh token
            var now = nowProvider.UtcNow;
            RefreshTokens[refreshToken] = new RefreshTokenInfo(
                UserId: user.Id,
                Email: $"{user.Username}@{user.Domain.Name}",
                CreatedAt: now,
                ExpiresAt: now.AddDays(30) // Refresh tokens expire in 30 days
            );

            logger.LogInformation("Login successful for user: {Email}", request.Email);

            return new LoginResponse {
                Token = accessToken,
                ExpiresAt = expiresAt,
                User = sessionInfo,
                RefreshToken = refreshToken
            };
        } catch (Exception ex) {
            logger.LogError(ex, "Error during login for user: {Email}", request.Email);
            return null;
        }
    }

    public async Task<bool> LogoutAsync(string userId) {
        logger.LogInformation("Logout request for user ID: {UserId}", userId);

        try {
            // Remove all refresh tokens for this user
            var tokensToRemove = RefreshTokens
                .Where(kvp => kvp.Value.UserId.ToString() == userId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in tokensToRemove) {
                RefreshTokens.TryRemove(token, out _);
            }

            logger.LogInformation("Logout successful for user ID: {UserId}", userId);
            return true;
        } catch (Exception ex) {
            logger.LogError(ex, "Error during logout for user ID: {UserId}", userId);
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
            var userInfo = await userService.GetUserEntityByEmailAsync(email);
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
            var newToken = jwtService.GenerateAccessToken(sessionInfo);
            var expiresAt = jwtService.GetTokenExpiration();

            return new SessionResponse {
                IsAuthenticated = true,
                User = sessionInfo,
                ExpiresAt = expiresAt,
                Token = newToken
            };
        } catch (Exception ex) {
            logger.LogError(ex, "Error getting current session");
            return new SessionResponse { IsAuthenticated = false };
        }
    }

    public async Task<LoginResponse> RefreshTokenAsync(string refreshToken) {
        logger.LogInformation("Refresh token request");

        try {
            if (!RefreshTokens.TryGetValue(refreshToken, out var tokenInfo)) {
                logger.LogWarning("Invalid refresh token provided");
                return null;
            }

            if (tokenInfo.ExpiresAt < nowProvider.UtcNow) {
                logger.LogWarning("Refresh token expired");
                RefreshTokens.TryRemove(refreshToken, out _);
                return null;
            }

            // Get current user information
            var user = await userService.GetUserEntityByEmailAsync(tokenInfo.Email);
            if (user == null || !user.CanLogin) {
                logger.LogWarning("User not found or disabled during token refresh: {Email}", tokenInfo.Email);
                RefreshTokens.TryRemove(refreshToken, out _);
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
            var accessToken = jwtService.GenerateAccessToken(sessionInfo);
            var newRefreshToken = jwtService.GenerateRefreshToken();
            var expiresAt = jwtService.GetTokenExpiration();

            // Replace old refresh token with new one
            RefreshTokens.TryRemove(refreshToken, out _);
            var now = nowProvider.UtcNow;
            RefreshTokens[newRefreshToken] = new RefreshTokenInfo(
                UserId: user.Id,
                Email: $"{user.Username}@{user.Domain.Name}",
                CreatedAt: now,
                ExpiresAt: now.AddDays(30)
            );

            logger.LogInformation("Token refresh successful for user: {Email}", $"{user.Username}@{user.Domain.Name}");

            return new LoginResponse {
                Token = accessToken,
                ExpiresAt = expiresAt,
                User = sessionInfo,
                RefreshToken = newRefreshToken
            };
        } catch (Exception ex) {
            logger.LogError(ex, "Error during token refresh");
            return null;
        }
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken) {
        logger.LogInformation("Refresh token revocation request");

        try {
            var removed = RefreshTokens.TryRemove(refreshToken, out _);
            logger.LogInformation("Refresh token revocation result: {Removed}", removed);
            return removed;
        } catch (Exception ex) {
            logger.LogError(ex, "Error during refresh token revocation");
            return false;
        }
    }

    private record RefreshTokenInfo(int UserId, string Email, DateTime CreatedAt, DateTime ExpiresAt);
}
