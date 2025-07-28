using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.DTOs.Folder;
using Frimerki.Models.Entities;
using Frimerki.Server;

namespace Frimerki.Tests.Integration;

public class FoldersControllerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly EmailDbContext _context;
    private readonly string _jwtToken;

    public FoldersControllerTests(WebApplicationFactory<Program> factory) {
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
        _jwtToken = GetValidJwtTokenAsync().Result;

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _jwtToken);
    }

    private void SeedTestData() {
        var domain = new Domain {
            Id = 1,
            Name = "example.com",
            IsActive = true,
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

        var folders = new List<Frimerki.Models.Entities.Folder> {
            new() {
                Id = 1,
                UserId = 1,
                Name = "INBOX",
                SystemFolderType = "INBOX",
                UidNext = 1,
                UidValidity = 1,
                Exists = 5,
                Recent = 1,
                Unseen = 2,
                Subscribed = true
            },
            new() {
                Id = 2,
                UserId = 1,
                Name = "SENT",
                SystemFolderType = "SENT",
                UidNext = 1,
                UidValidity = 1,
                Exists = 3,
                Recent = 0,
                Unseen = 0,
                Subscribed = true
            },
            new() {
                Id = 3,
                UserId = 1,
                Name = "INBOX/Work",
                SystemFolderType = null,
                UidNext = 1,
                UidValidity = 1,
                Exists = 2,
                Recent = 0,
                Unseen = 1,
                Subscribed = false
            }
        };

        _context.Domains.Add(domain);
        _context.Users.Add(user);
        _context.Folders.AddRange(folders);
        _context.SaveChanges();
    }

    private async Task<string> GetValidJwtTokenAsync() {
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var response = await _client.PostAsJsonAsync("/api/session", loginRequest);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        return loginResult!.Token;
    }

    [Fact]
    public async Task GetFolders_ReturnsAllUserFolders() {
        // Act
        var response = await _client.GetAsync("/api/folders");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<FolderListResponse>>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);

        var inbox = result.First(f => f.Name == "INBOX");
        Assert.Equal("INBOX", inbox.SystemFolderType);
        Assert.Equal(5, inbox.MessageCount);
        Assert.Equal(2, inbox.UnseenCount);
        Assert.True(inbox.Subscribed);
    }

    [Fact]
    public async Task GetFolder_WithValidName_ReturnsFolder() {
        // Act
        var response = await _client.GetAsync("/api/folders/INBOX");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FolderResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("INBOX", result.Name);
        Assert.Equal("INBOX", result.SystemFolderType);
        Assert.Equal(5, result.Exists);
        Assert.Equal(1, result.Recent);
        Assert.Equal(2, result.Unseen);
        Assert.True(result.Subscribed);
    }

    [Fact]
    public async Task GetFolder_WithEncodedName_ReturnsFolder() {
        // Act
        var response = await _client.GetAsync("/api/folders/INBOX%2FWork");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FolderResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("INBOX/Work", result.Name);
        Assert.Null(result.SystemFolderType);
        Assert.False(result.Subscribed);
    }

    [Fact]
    public async Task GetFolder_WithInvalidName_ReturnsNotFound() {
        // Act
        var response = await _client.GetAsync("/api/folders/NonExistent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_WithValidRequest_CreatesFolder() {
        // Arrange
        var request = new FolderRequest {
            Name = "INBOX/Projects",
            Subscribed = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/folders", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FolderResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("INBOX/Projects", result.Name);
        Assert.True(result.Subscribed);
    }

    [Fact]
    public async Task CreateFolder_WithExistingName_ReturnsBadRequest() {
        // Arrange
        var request = new FolderRequest {
            Name = "INBOX"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/folders", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_WithInvalidRequest_ReturnsBadRequest() {
        // Arrange
        var request = new FolderRequest {
            Name = "" // Empty name should be invalid
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/folders", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFolder_WithValidRequest_UpdatesFolder() {
        // Arrange
        var request = new FolderUpdateRequest {
            Name = "INBOX/Business",
            Subscribed = true
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/folders/INBOX%2FWork", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FolderResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("INBOX/Business", result.Name);
        Assert.True(result.Subscribed);
    }

    [Fact]
    public async Task UpdateFolder_WithSystemFolder_ReturnsBadRequest() {
        // Arrange
        var request = new FolderUpdateRequest {
            Name = "MyInbox"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/folders/INBOX", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFolder_WithInvalidName_ReturnsNotFound() {
        // Arrange
        var request = new FolderUpdateRequest {
            Name = "NewName"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/folders/NonExistent", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_WithValidName_DeletesFolder() {
        // Act
        var response = await _client.DeleteAsync("/api/folders/INBOX%2FWork");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify folder is deleted
        var getResponse = await _client.GetAsync("/api/folders/INBOX%2FWork");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_WithSystemFolder_ReturnsBadRequest() {
        // Act
        var response = await _client.DeleteAsync("/api/folders/INBOX");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_WithInvalidName_ReturnsNotFound() {
        // Act
        var response = await _client.DeleteAsync("/api/folders/NonExistent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFolders_WithoutAuth_ReturnsUnauthorized() {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync("/api/folders");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_WithoutAuth_ReturnsUnauthorized() {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;
        var request = new FolderRequest { Name = "Test" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/folders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() {
        _context?.Dispose();
        _client?.Dispose();
    }
}
