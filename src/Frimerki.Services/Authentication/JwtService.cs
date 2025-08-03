using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Frimerki.Models.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Frimerki.Services.Authentication;

public interface IJwtService {
    string GenerateAccessToken(UserSessionInfo user);
    string GenerateRefreshToken();
    DateTime GetTokenExpiration(bool rememberMe = false);
}

public class JwtService(IConfiguration configuration, ILogger<JwtService> logger) : IJwtService {
    public string GenerateAccessToken(UserSessionInfo user) {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtSecret()));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        List<Claim> claims = [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("domain_id", user.DomainId.ToString()),
            new("domain_name", user.DomainName),
            new("can_receive", user.CanReceive.ToString()),
            new("can_login", user.CanLogin.ToString())
        ];

        if (!string.IsNullOrEmpty(user.FullName)) {
            claims.Add(new Claim(ClaimTypes.GivenName, user.FullName));
        }

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "Frimerki",
            audience: configuration["Jwt:Audience"] ?? "Frimerki",
            claims: claims,
            expires: GetTokenExpiration(),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public DateTime GetTokenExpiration(bool rememberMe = false) {
        // 30 days if remember me, 8 hours otherwise
        var expirationHours = rememberMe ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8);
        return DateTime.UtcNow.Add(expirationHours);
    }

    private string GetJwtSecret() {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrEmpty(secret)) {
            // Generate a random secret for development
            secret = GenerateRefreshToken();
            logger.LogWarning("JWT secret not configured, using generated secret. This should be set in production.");
        }
        return secret;
    }
}
