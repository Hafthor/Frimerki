using Frimerki.Models.DTOs;
using Frimerki.Services.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Frimerki.Tests.Services.Authentication;

public class JwtServiceTests {
    private readonly JwtService _jwtService;

    public JwtServiceTests() {
        var mockLogger = new Mock<ILogger<JwtService>>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> {
                ["Jwt:Secret"] = "ThisIsAVeryLongSecretKeyForTesting123456789012345678901234567890",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();

        _jwtService = new JwtService(configuration, mockLogger.Object);
    }

    [Fact]
    public void GenerateAccessToken_WithValidUser_ReturnsValidToken() {
        // Arrange
        var user = new UserSessionInfo {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            Role = "User",
            DomainId = 1,
            DomainName = "example.com",
            CanReceive = true,
            CanLogin = true,
            FullName = "Test User"
        };

        // Act
        var token = _jwtService.GenerateAccessToken(user);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsUniqueTokens() {
        // Act
        var token1 = _jwtService.GenerateRefreshToken();
        var token2 = _jwtService.GenerateRefreshToken();

        // Assert
        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotEmpty(token1);
        Assert.NotEmpty(token2);
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void GetTokenExpiration_WithoutRememberMe_Returns8Hours() {
        // Act
        var expiration = _jwtService.GetTokenExpiration();

        // Assert
        var expectedExpiration = DateTime.UtcNow.AddHours(8);
        Assert.True(Math.Abs((expiration - expectedExpiration).TotalMinutes) < 1);
    }

    [Fact]
    public void GetTokenExpiration_WithRememberMe_Returns30Days() {
        // Act
        var expiration = _jwtService.GetTokenExpiration(true);

        // Assert
        var expectedExpiration = DateTime.UtcNow.AddHours(24 * 30);
        Assert.True(Math.Abs((expiration - expectedExpiration).TotalMinutes) < 1);
    }
}
