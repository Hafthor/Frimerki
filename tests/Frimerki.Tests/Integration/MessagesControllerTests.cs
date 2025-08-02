using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Server;
using Frimerki.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frimerki.Tests.Integration;

public class MessagesControllerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _jwtToken;
    private readonly string _globalDatabaseName;
    private readonly string _domainDatabaseName;

    public MessagesControllerTests(WebApplicationFactory<Program> factory) {
        _globalDatabaseName = "GlobalTestDatabase_" + Guid.NewGuid();
        _domainDatabaseName = "DomainTestDatabase_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                // Remove existing DbContext registrations
                var emailDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EmailDbContext>));
                if (emailDescriptor != null) {
                    services.Remove(emailDescriptor);
                }

                var globalDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GlobalDbContext>));
                if (globalDescriptor != null) {
                    services.Remove(globalDescriptor);
                }

                // Remove default email server hosted services to prevent port conflicts
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType?.Name.Contains("Server") == true)).ToList();
                foreach (var service in hostedServices) {
                    services.Remove(service);
                }

                // Add in-memory databases for testing
                services.AddDbContext<GlobalDbContext>(options => {
                    options.UseInMemoryDatabase(_globalDatabaseName);
                });

                services.AddDbContext<EmailDbContext>(options => {
                    options.UseInMemoryDatabase(_domainDatabaseName);
                });

                // Override the domain DB context factory for testing
                services.AddSingleton<IDomainDbContextFactory>(provider => {
                    return new TestDomainDbContextFactory(_domainDatabaseName);
                });
            });
        });

        _client = _factory.CreateClient();

        // Seed test data and get JWT token
        SeedTestData();
        _jwtToken = GetValidJwtTokenAsync().Result;

        // Set authorization header
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _jwtToken);
    }

    private void SeedTestData() {
        using var scope = _factory.Services.CreateScope();
        var globalContext = scope.ServiceProvider.GetRequiredService<GlobalDbContext>();
        var emailContext = scope.ServiceProvider.GetRequiredService<EmailDbContext>();

        // Add domain to global registry
        var domainRegistry = new DomainRegistry {
            Id = 1,
            Name = "example.com",
            DatabaseName = _domainDatabaseName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        globalContext.DomainRegistry.Add(domainRegistry);
        globalContext.SaveChanges();

        // Add domain settings to domain-specific database
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

        var folders = new List<Frimerki.Models.Entities.Folder> {
            new() {
                Id = 1,
                UserId = 1,
                Name = "INBOX",
                SystemFolderType = "INBOX",
                UidNext = 2,
                UidValidity = 1,
                Exists = 1,
                Subscribed = true
            },
            new() {
                Id = 2,
                UserId = 1,
                Name = "SENT",
                SystemFolderType = "SENT",
                UidNext = 1,
                UidValidity = 1,
                Exists = 0,
                Subscribed = true
            },
            new() {
                Id = 3,
                UserId = 1,
                Name = "TRASH",
                SystemFolderType = "TRASH",
                UidNext = 1,
                UidValidity = 1,
                Exists = 0,
                Subscribed = true
            }
        };

        var message = new Frimerki.Models.Entities.Message {
            Id = 1,
            HeaderMessageId = "<test@example.com>",
            FromAddress = "sender@example.com",
            ToAddress = "testuser@example.com",
            Subject = "Test Message",
            Headers = "From: sender@example.com\r\nTo: testuser@example.com\r\nSubject: Test Message\r\n",
            Body = "This is a test message.",
            MessageSize = 100,
            ReceivedAt = DateTime.UtcNow,
            SentDate = DateTime.UtcNow,
            Uid = 1,
            UidValidity = 1,
            Envelope = "{\"subject\":\"Test Message\",\"from\":[{\"email\":\"sender@example.com\"}]}",
            BodyStructure = "{\"type\":\"text\",\"subtype\":\"plain\"}"
        };

        var userMessage = new UserMessage {
            Id = 1,
            UserId = 1,
            MessageId = 1,
            FolderId = 1,
            Uid = 1,
            ReceivedAt = DateTime.UtcNow
        };

        var messageFlag = new MessageFlag {
            Id = 1,
            MessageId = 1,
            UserId = 1,
            FlagName = "\\Seen",
            IsSet = false
        };

        emailContext.Domains.Add(domain);
        emailContext.Users.Add(user);
        emailContext.Folders.AddRange(folders);
        emailContext.Messages.Add(message);
        emailContext.UserMessages.Add(userMessage);
        emailContext.MessageFlags.Add(messageFlag);
        emailContext.SaveChanges();
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
    public async Task GetMessages_WithoutFilters_ReturnsUserMessages() {
        // Act
        var response = await _client.GetAsync("/api/messages");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaginatedInfo<MessageListItemResponse>>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("Test Message", result.Items[0].Subject);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetMessages_WithFolderFilter_ReturnsFilteredMessages() {
        // Act
        var response = await _client.GetAsync("/api/messages?folder=INBOX");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaginatedInfo<MessageListItemResponse>>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Contains("folder", result.AppliedFilters!.Keys);
    }

    [Fact]
    public async Task GetMessages_WithPagination_ReturnsCorrectPage() {
        // Act
        var response = await _client.GetAsync("/api/messages?skip=0&take=10");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaginatedInfo<MessageListItemResponse>>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(0, result.Skip);
        Assert.Equal(10, result.Take);
        Assert.Null(result.NextUrl); // Only 1 message, so no next skip
    }

    [Fact]
    public async Task GetMessage_WithValidId_ReturnsMessage() {
        // Act
        var response = await _client.GetAsync("/api/messages/1");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MessageResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Test Message", result.Subject);
        Assert.Equal("sender@example.com", result.FromAddress);
        Assert.NotNull(result.Envelope);
        Assert.NotNull(result.BodyStructure);
        Assert.NotNull(result.Flags);
    }

    [Fact]
    public async Task GetMessage_WithInvalidId_ReturnsNotFound() {
        // Act
        var response = await _client.GetAsync("/api/messages/999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateMessage_WithValidRequest_CreatesMessage() {
        // Arrange
        var request = new MessageRequest {
            ToAddress = "recipient@example.com",
            Subject = "New Test Message",
            Body = "This is a new test message."
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/messages", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MessageResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("New Test Message", result.Subject);
        Assert.Equal("recipient@example.com", result.ToAddress);
        Assert.Equal("SENT", result.Folder);
    }

    [Fact]
    public async Task CreateMessage_WithInvalidRequest_ReturnsBadRequest() {
        // Arrange
        var request = new MessageRequest {
            // Missing required ToAddress
            Subject = "Test",
            Body = "Test body"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/messages", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMessage_WithValidRequest_UpdatesMessage() {
        // Arrange
        var request = new MessageUpdateRequest {
            Flags = new MessageFlagsRequest {
                Seen = true,
                Flagged = true
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/messages/1", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MessageResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.True(result.Flags.Seen);
        Assert.True(result.Flags.Flagged);
    }

    [Fact]
    public async Task UpdateMessage_WithInvalidId_ReturnsNotFound() {
        // Arrange
        var request = new MessageUpdateRequest {
            Flags = new MessageFlagsRequest { Seen = true }
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/messages/999", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMessage_WithValidId_DeletesMessage() {
        // Act
        var response = await _client.DeleteAsync("/api/messages/1");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify message is moved to trash
        var getResponse = await _client.GetAsync("/api/messages/1");
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MessageResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("TRASH", result.Folder);
        Assert.True(result.Flags.Deleted);
    }

    [Fact]
    public async Task DeleteMessage_WithInvalidId_ReturnsNotFound() {
        // Act
        var response = await _client.DeleteAsync("/api/messages/999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_WithoutAuth_ReturnsUnauthorized() {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync("/api/messages");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_WithInvalidToken_ReturnsUnauthorized() {
        // Arrange
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");

        // Act
        var response = await _client.GetAsync("/api/messages");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() {
        _client?.Dispose();
        _factory?.Dispose();
    }
}
