using Frimerki.Models.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Frimerki.Services.Authentication;

public interface IJwtService {
    string GenerateAccessToken(UserSessionInfo user);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
    DateTime GetTokenExpiration(bool rememberMe = false);
}

public class JwtService : IJwtService {
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtService> _logger;

    public JwtService(IConfiguration configuration, ILogger<JwtService> logger) {
        _configuration = configuration;
        _logger = logger;
    }

    public string GenerateAccessToken(UserSessionInfo user) {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtSecret()));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("domain_id", user.DomainId.ToString()),
            new("domain_name", user.DomainName),
            new("can_receive", user.CanReceive.ToString()),
            new("can_login", user.CanLogin.ToString())
        };

        if (!string.IsNullOrEmpty(user.FullName)) {
            claims.Add(new Claim(ClaimTypes.GivenName, user.FullName));
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "Frimerki",
            audience: _configuration["Jwt:Audience"] ?? "Frimerki",
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

    public ClaimsPrincipal? ValidateToken(string token) {
        try {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(GetJwtSecret());

            var validationParameters = new TokenValidationParameters {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"] ?? "Frimerki",
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"] ?? "Frimerki",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public DateTime GetTokenExpiration(bool rememberMe = false) {
        var expirationHours = rememberMe ? 24 * 30 : 8; // 30 days if remember me, 8 hours otherwise
        return DateTime.UtcNow.AddHours(expirationHours);
    }

    private string GetJwtSecret() {
        var secret = _configuration["Jwt:Secret"];
        if (string.IsNullOrEmpty(secret)) {
            // Generate a random secret for development
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            secret = Convert.ToBase64String(bytes);
            _logger.LogWarning("JWT secret not configured, using generated secret. This should be set in production.");
        }
        return secret;
    }
}
