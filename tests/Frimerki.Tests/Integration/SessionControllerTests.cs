using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frimerki.Tests.Integration;

// Test implementation of IDomainDbContextFactory for integration tests
public class TestDomainDbContextFactory(string databaseName) : IDomainDbContextFactory {
    public DomainDbContext CreateDbContext(string domainName) {
        var options = new DbContextOptionsBuilder<DomainDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new DomainDbContext(options, domainName);
    }

    public async Task<DomainDbContext> CreateDbContextAsync(string domainName) => CreateDbContext(domainName);

    public async Task EnsureDatabaseExistsAsync(string domainName) { }
}

public class SessionControllerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _domainDatabaseName;

    public SessionControllerTests(WebApplicationFactory<Program> factory) {
        var globalDatabaseName = "GlobalTestDatabase_" + Guid.NewGuid();
        _domainDatabaseName = "DomainTestDatabase_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                // Remove existing DbContext registrations
                var globalDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GlobalDbContext>));
                if (globalDescriptor != null) {
                    services.Remove(globalDescriptor);
                }

                var emailDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EmailDbContext>));
                if (emailDescriptor != null) {
                    services.Remove(emailDescriptor);
                }

                // Remove default email server hosted services to prevent port conflicts
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType?.Name.Contains("Server") == true)).ToList();
                foreach (var service in hostedServices) {
                    services.Remove(service);
                }

                // Add test databases
                services.AddDbContext<GlobalDbContext>(options => {
                    options.UseInMemoryDatabase(globalDatabaseName);
                });

                // Add legacy EmailDbContext for services that still use it
                services.AddDbContext<EmailDbContext>(options => {
                    options.UseInMemoryDatabase(_domainDatabaseName);
                });

                // Override the domain DB context factory for testing
                services.AddSingleton<IDomainDbContextFactory>(_ => new TestDomainDbContextFactory(_domainDatabaseName));
            });
        });

        _client = _factory.CreateClient();

        SeedTestData();
    }

    private void SeedTestData() {
        using var scope = _factory.Services.CreateScope();

        // First, set up the global database with domain registry
        var globalContext = scope.ServiceProvider.GetRequiredService<GlobalDbContext>();
        var domainRegistry = new DomainRegistry {
            Name = "example.com",
            DatabaseName = _domainDatabaseName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        globalContext.DomainRegistry.Add(domainRegistry);
        globalContext.SaveChanges();

        // Set up the legacy EmailDbContext that services still use
        var emailContext = scope.ServiceProvider.GetRequiredService<EmailDbContext>();

        var domain = new DomainSettings {
            Id = 1,
            Name = "example.com",
            CreatedAt = DateTime.UtcNow
        };

        // Create password hash that matches the service implementation
        var salt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
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

        emailContext.Domains.Add(domain);
        emailContext.Users.Add(user);
        emailContext.SaveChanges();
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
        _client?.Dispose();
        _factory?.Dispose();
    }
}
