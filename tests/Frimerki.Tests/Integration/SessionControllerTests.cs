using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Server;

namespace Frimerki.Tests.Integration;

public class SessionControllerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly EmailDbContext _context;

    public SessionControllerTests(WebApplicationFactory<Program> factory) {
        _factory = factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EmailDbContext>));
                if (descriptor != null) {
                    services.Remove(descriptor);
                }

                services.AddDbContext<EmailDbContext>(options => {
                    options.UseInMemoryDatabase("TestDatabase_" + Guid.NewGuid());
                });
            });
        });

        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<EmailDbContext>();

        SeedTestData();
    }

    private void SeedTestData() {
        var domain = new Domain {
            Id = 1,
            Name = "example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Generate a proper salt
        var saltBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(saltBytes);
        var salt = Convert.ToBase64String(saltBytes);

        // Hash the password correctly - match the service implementation exactly
        string passwordHash;
        using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes("password123", Convert.FromBase64String(salt), 10000, System.Security.Cryptography.HashAlgorithmName.SHA256)) {
            var hash = pbkdf2.GetBytes(32);
            passwordHash = Convert.ToBase64String(hash);
        }

        var user = new User {
            Id = 1,
            Username = "testuser",
            DomainId = 1,
            PasswordHash = passwordHash,
            Salt = salt,
            FullName = "Test User",
            Role = "User",
            CanReceive = true,
            CanLogin = true,
            CreatedAt = DateTime.UtcNow,
            Domain = domain
        };

        _context.Domains.Add(domain);
        _context.Users.Add(user);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CreateSession_WithValidCredentials_ReturnsToken() {
        // Arrange
        var request = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("testuser@example.com", result.User.Email);
        Assert.Equal("Test User", result.User.FullName);
    }

    [Fact]
    public async Task CreateSession_WithInvalidCredentials_ReturnsBadRequest() {
        // Arrange
        var request = new LoginRequest {
            Email = "testuser@example.com",
            Password = "wrongpassword"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_WithInvalidEmail_ReturnsBadRequest() {
        // Arrange
        var request = new LoginRequest {
            Email = "nonexistent@example.com",
            Password = "password123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_WithMissingEmail_ReturnsBadRequest() {
        // Arrange
        var request = new LoginRequest {
            Email = "",
            Password = "password123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_WithRememberMe_ReturnsLongerExpiry() {
        // Arrange
        var request = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123",
            RememberMe = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.True(result.ExpiresAt > DateTime.UtcNow.AddDays(20)); // Should be ~30 days
    }

    [Fact]
    public async Task GetSession_WithValidToken_ReturnsUserInfo() {
        // Arrange - First login to get a token
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(loginContent, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult!.Token);

        // Act
        var response = await _client.GetAsync("/api/session");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SessionResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("testuser@example.com", result.User!.Email);
        Assert.Equal("Test User", result.User.FullName);
        Assert.Equal("User", result.User.Role);
    }

    [Fact]
    public async Task GetSession_WithoutToken_ReturnsUnauthorized() {
        // Act
        var response = await _client.GetAsync("/api/session");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSession_WithInvalidToken_ReturnsUnauthorized() {
        // Arrange
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");

        // Act
        var response = await _client.GetAsync("/api/session");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_WithValidToken_ReturnsSuccess() {
        // Arrange - First login to get a token
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(loginContent, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult!.Token);

        // Act
        var response = await _client.DeleteAsync("/api/session");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LogoutResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetSessionStatus_WithValidToken_ReturnsAuthenticated() {
        // Arrange - First login to get a token
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(loginContent, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult!.Token);

        // Act
        var response = await _client.GetAsync("/api/session/status");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        Assert.True(root.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("testuser@example.com", root.GetProperty("email").GetString());
        Assert.Equal("User", root.GetProperty("role").GetString());
    }

    [Fact]
    public async Task GetSessionStatus_WithoutToken_ReturnsNotAuthenticated() {
        // Act
        var response = await _client.GetAsync("/api/session/status");

        // Assert
        response.EnsureSuccessStatusCode(); // Status endpoint should return 200 even when not authenticated
        var content = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        Assert.False(root.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewToken() {
        // Arrange - First login to get tokens
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(loginContent, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        var refreshRequest = new RefreshTokenRequest {
            RefreshToken = loginResult!.RefreshToken
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session/refresh", refreshRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("testuser@example.com", result.User.Email);
    }

    [Fact]
    public async Task RevokeToken_WithValidToken_ReturnsSuccess() {
        // Arrange - First login to get tokens
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(loginContent, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        var revokeRequest = new RefreshTokenRequest {
            RefreshToken = loginResult!.RefreshToken
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/session/revoke", revokeRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
    }

    public void Dispose() {
        _context?.Dispose();
        _client?.Dispose();
    }
}
