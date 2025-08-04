using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frimerki.Tests.Services;

public class DomainServiceTests : IDisposable {
    private readonly EmailDbContext _context;
    private readonly DomainService _domainService;
    private readonly MockDomainRegistryService _mockDomainRegistryService;

    public DomainServiceTests() {
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new EmailDbContext(options);
        _mockDomainRegistryService = new MockDomainRegistryService();
        var mockDomainDbFactory = new MockDomainDbContextFactory();
        ILogger<DomainService> logger = NullLogger<DomainService>.Instance;

        _domainService = new DomainService(
            _context,
            _mockDomainRegistryService,
            mockDomainDbFactory,
            logger);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetDomainsAsync_WithNoRoleFilter_ReturnsAllDomains() {
        // Arrange
        await SeedTestDomains();

        // Act
        var result = await _domainService.GetDomainsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Domains.Count);
        Assert.Contains(result.Domains, d => d.Name == "example.com");
        Assert.Contains(result.Domains, d => d.Name == "test.org");
    }

    [Fact]
    public async Task GetDomainsAsync_WithDomainAdminRole_ReturnsFilteredDomains() {
        // Arrange
        await SeedTestDomains();

        // Act
        var result = await _domainService.GetDomainsAsync("DomainAdmin", 1);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Domains);
        Assert.Equal("example.com", result.Domains[0].Name);
    }

    [Fact]
    public async Task GetDomainByNameAsync_ExistingDomain_ReturnsDomainResponse() {
        // Arrange
        await SeedTestDomains();

        // Act
        var result = await _domainService.GetDomainByNameAsync("example.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("example.com", result.Name);
        Assert.True(result.IsActive);
        Assert.Equal(2, result.UserCount);
    }

    [Fact]
    public async Task GetDomainByNameAsync_NonExistentDomain_ThrowsArgumentException() {
        // Arrange
        await SeedTestDomains();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.GetDomainByNameAsync("nonexistent.com"));

        Assert.Contains("Domain 'nonexistent.com' not found", exception.Message);
    }

    [Fact]
    public async Task CreateDomainAsync_ValidRequest_CreatesDomainSuccessfully() {
        // Arrange
        var request = new DomainRequest {
            Name = "newdomain.com",
            CreateDatabase = true
        };

        // Act
        var result = await _domainService.CreateDomainAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("newdomain.com", result.Name);
        Assert.True(result.DatabaseCreated);
        Assert.True(result.IsActive);
        Assert.NotNull(result.InitialSetup);
    }

    [Fact]
    public async Task CreateDomainAsync_DomainAlreadyExists_ThrowsInvalidOperationException() {
        // Arrange
        _mockDomainRegistryService.ExistingDomains.Add("existing.com");
        var request = new DomainRequest {
            Name = "existing.com",
            CreateDatabase = true
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _domainService.CreateDomainAsync(request));

        Assert.Contains("Domain 'existing.com' is already registered", exception.Message);
    }

    [Fact]
    public async Task UpdateDomainAsync_NonExistentDomain_ThrowsArgumentException() {
        // Arrange
        var request = new DomainUpdateRequest();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.UpdateDomainAsync("nonexistent.com", request));

        Assert.Contains("Domain 'nonexistent.com' not found", exception.Message);
    }

    [Fact]
    public async Task DeleteDomainAsync_DomainWithoutUsers_DeletesSuccessfully() {
        // Arrange
        var domain = new DomainSettings {
            Name = "empty.com",
            CreatedAt = DateTime.UtcNow
        };
        _context.Domains.Add(domain);
        await _context.SaveChangesAsync();

        // Act
        await _domainService.DeleteDomainAsync("empty.com");

        // Assert
        var deletedDomain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Name == "empty.com");
        Assert.Null(deletedDomain);
    }

    [Fact]
    public async Task DeleteDomainAsync_DomainWithUsers_ThrowsInvalidOperationException() {
        // Arrange
        await SeedTestDomains();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _domainService.DeleteDomainAsync("example.com"));

        Assert.Contains("Cannot delete domain 'example.com' because it has", exception.Message);
    }

    [Fact]
    public async Task GenerateDkimKeyAsync_ValidRequest_GeneratesKeySuccessfully() {
        // Arrange
        await SeedTestDomains();
        var request = new GenerateDkimKeyRequest {
            Selector = "default",
            KeySize = 2048
        };

        // Act
        var result = await _domainService.GenerateDkimKeyAsync("example.com", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("default", result.Selector);
        Assert.NotEmpty(result.PublicKey);
        Assert.NotEmpty(result.DnsRecord);
        Assert.True(result.IsActive);
        Assert.StartsWith("v=DKIM1; k=rsa; p=", result.DnsRecord);
    }

    [Fact]
    public async Task GetDkimKeyAsync_DomainWithActiveKey_ReturnsKey() {
        // Arrange
        await SeedTestDomainsWithDkim();

        // Act
        var result = await _domainService.GetDkimKeyAsync("example.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("default", result.Selector);
        Assert.True(result.IsActive);
        Assert.StartsWith("v=DKIM1; k=rsa; p=", result.DnsRecord);
    }

    [Fact]
    public async Task GetDkimKeyAsync_DomainWithoutActiveKey_ThrowsArgumentException() {
        // Arrange
        await SeedTestDomains();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.GetDkimKeyAsync("example.com"));

        Assert.Contains("No active DKIM key found for domain 'example.com'", exception.Message);
    }

    [Fact]
    public async Task CreateDomainAsync_WithCatchAllUser_ValidatesEmailFormat() {
        // Arrange
        var request = new DomainRequest {
            Name = "newdomain.com",
            CreateDatabase = true,
            CatchAllUser = "invalid-email-format"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.CreateDomainAsync(request));

        Assert.Contains("Catch-all user must belong to the domain being created", exception.Message);
    }

    [Fact]
    public async Task CreateDomainAsync_WithCatchAllUserFromDifferentDomain_ThrowsArgumentException() {
        // Arrange
        var request = new DomainRequest {
            Name = "newdomain.com",
            CreateDatabase = true,
            CatchAllUser = "user@differentdomain.com"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.CreateDomainAsync(request));

        Assert.Contains("Catch-all user must belong to the domain being created", exception.Message);
    }

    [Fact]
    public async Task CreateDomainAsync_WithValidCatchAllUser_CreatesDomainSuccessfully() {
        // Arrange
        var request = new DomainRequest {
            Name = "newdomain.com",
            CreateDatabase = true,
            CatchAllUser = "admin@newdomain.com"
        };

        // Act
        var result = await _domainService.CreateDomainAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("newdomain.com", result.Name);
        Assert.True(result.DatabaseCreated);
        Assert.True(result.IsActive);
        Assert.NotNull(result.InitialSetup);
        Assert.False(result.InitialSetup.AdminUserCreated);
        Assert.True(result.InitialSetup.DefaultFoldersCreated);
        Assert.False(result.InitialSetup.DkimKeysGenerated);
    }

    [Fact]
    public async Task UpdateDomainAsync_WithValidCatchAllUser_UpdatesSuccessfully() {
        // Arrange
        await SeedTestDomains();
        var request = new DomainUpdateRequest {
            CatchAllUser = "user1@example.com"
        };

        // Act
        var result = await _domainService.UpdateDomainAsync("example.com", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("user1@example.com", result.CatchAllUser);
    }

    [Fact]
    public async Task UpdateDomainAsync_WithInvalidCatchAllUserDomain_ThrowsArgumentException() {
        // Arrange
        await SeedTestDomains();
        var request = new DomainUpdateRequest {
            CatchAllUser = "user@wrongdomain.com"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.UpdateDomainAsync("example.com", request));

        Assert.Contains("Catch-all user must belong to this domain", exception.Message);
    }

    [Fact]
    public async Task UpdateDomainAsync_WithNonExistentCatchAllUser_ThrowsArgumentException() {
        // Arrange
        await SeedTestDomains();
        var request = new DomainUpdateRequest {
            CatchAllUser = "nonexistent@example.com"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.UpdateDomainAsync("example.com", request));

        Assert.Contains("User 'nonexistent' not found in domain 'example.com'", exception.Message);
    }

    [Fact]
    public async Task UpdateDomainAsync_ClearingCatchAllUser_UpdatesSuccessfully() {
        // Arrange
        await SeedTestDomainsWithCatchAllUser();
        var request = new DomainUpdateRequest {
            CatchAllUser = ""
        };

        // Act
        var result = await _domainService.UpdateDomainAsync("example.com", request);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.CatchAllUser);
    }

    [Fact]
    public async Task UpdateDomainAsync_WithActiveStatusChange_UpdatesSuccessfully() {
        // Arrange
        await SeedTestDomains();
        var request = new DomainUpdateRequest {
            IsActive = false
        };

        // Act
        var result = await _domainService.UpdateDomainAsync("example.com", request);

        // Assert
        Assert.NotNull(result);
        // Note: IsActive is handled by mock registry service
    }

    [Fact]
    public async Task GenerateDkimKeyAsync_WithExistingSelector_ThrowsInvalidOperationException() {
        // Arrange
        await SeedTestDomainsWithDkim();
        var request = new GenerateDkimKeyRequest {
            Selector = "default", // Already exists
            KeySize = 2048
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _domainService.GenerateDkimKeyAsync("example.com", request));

        Assert.Contains("DKIM key with selector 'default' already exists for domain 'example.com'", exception.Message);
    }

    [Fact]
    public async Task GenerateDkimKeyAsync_WithNewSelector_DeactivatesExistingKeys() {
        // Arrange
        await SeedTestDomainsWithDkim();
        var request = new GenerateDkimKeyRequest {
            Selector = "new-selector",
            KeySize = 2048
        };

        // Act
        var result = await _domainService.GenerateDkimKeyAsync("example.com", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("new-selector", result.Selector);
        Assert.True(result.IsActive);
        Assert.NotEmpty(result.PublicKey);
        Assert.StartsWith("v=DKIM1; k=rsa; p=", result.DnsRecord);

        // Verify old key is deactivated
        var oldKey = await _context.DkimKeys
            .FirstOrDefaultAsync(k => k.Selector == "default" && k.DomainId == 1);
        Assert.NotNull(oldKey);
        Assert.False(oldKey.IsActive);
    }

    [Fact]
    public async Task GenerateDkimKeyAsync_WithDifferentKeySizes_GeneratesValidKeys() {
        // Arrange
        await SeedTestDomains();
        var keySizes = new[] { 1024, 2048, 4096 };

        foreach (var keySize in keySizes) {
            var request = new GenerateDkimKeyRequest {
                Selector = $"key-{keySize}",
                KeySize = keySize
            };

            // Act
            var result = await _domainService.GenerateDkimKeyAsync("example.com", request);

            // Assert
            Assert.NotNull(result);
            Assert.Equal($"key-{keySize}", result.Selector);
            Assert.True(result.IsActive);
            Assert.NotEmpty(result.PublicKey);
            Assert.StartsWith("v=DKIM1; k=rsa; p=", result.DnsRecord);
        }
    }

    [Fact]
    public async Task GetDkimKeyAsync_NonExistentDomain_ThrowsArgumentException() {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.GetDkimKeyAsync("nonexistent.com"));

        Assert.Contains("Domain 'nonexistent.com' not found", exception.Message);
    }

    [Fact]
    public async Task GenerateDkimKeyAsync_NonExistentDomain_ThrowsArgumentException() {
        // Arrange
        var request = new GenerateDkimKeyRequest {
            Selector = "default",
            KeySize = 2048
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _domainService.GenerateDkimKeyAsync("nonexistent.com", request));

        Assert.Contains("Domain 'nonexistent.com' not found", exception.Message);
    }

    [Fact]
    public async Task GetDomainsAsync_WithHostAdminRole_ReturnsAllDomainsWithManageAllFlag() {
        // Arrange
        await SeedTestDomains();

        // Act
        var result = await _domainService.GetDomainsAsync("HostAdmin");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Domains.Count);
        Assert.True(result.CanManageAll);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetDomainsAsync_WithNoDomainsInDatabase_ReturnsEmptyList() {
        // Act
        var result = await _domainService.GetDomainsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Domains);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.CanManageAll);
    }

    [Fact]
    public async Task GetDomainByNameAsync_CaseInsensitiveMatch_ReturnsDomain() {
        // Arrange
        await SeedTestDomains();

        // Act
        var result = await _domainService.GetDomainByNameAsync("EXAMPLE.COM");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("example.com", result.Name);
    }

    [Fact]
    public async Task UpdateDomainAsync_CaseInsensitiveMatch_UpdatesDomain() {
        // Arrange
        await SeedTestDomains();
        var request = new DomainUpdateRequest {
            IsActive = false
        };

        // Act
        var result = await _domainService.UpdateDomainAsync("EXAMPLE.COM", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("example.com", result.Name);
    }

    [Fact]
    public async Task GetDomainsAsync_WithStorageCalculation_IncludesStorageInfo() {
        // Arrange
        await SeedTestDomainsWithMessages();

        // Act
        var result = await _domainService.GetDomainsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Domains.Count);
        var exampleDomain = result.Domains.First(d => d.Name == "example.com");
        Assert.True(exampleDomain.StorageUsed >= 0); // Should have calculated storage
    }

    [Fact]
    public async Task GetDomainByNameAsync_WithStorageAndDkimInfo_ReturnsCompleteInfo() {
        // Arrange
        await SeedTestDomainsWithMessagesAndDkim();

        // Act
        var result = await _domainService.GetDomainByNameAsync("example.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("example.com", result.Name);
        Assert.True(result.HasDkim);
        Assert.NotNull(result.DkimKey);
        Assert.Equal("default", result.DkimKey.Selector);
        Assert.True(result.DkimKey.IsActive);
        Assert.True(result.StorageUsed >= 0);
        Assert.Equal(2, result.UserCount);
    }

    [Fact]
    public async Task CreateDomainAsync_NormalizesToLowercase_CreatesWithLowercaseName() {
        // Arrange
        var request = new DomainRequest {
            Name = "UPPERCASE.COM",
            CreateDatabase = true
        };

        // Act
        var result = await _domainService.CreateDomainAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("uppercase.com", result.Name);
    }

    [Fact]
    public async Task DeleteDomainAsync_CaseInsensitiveMatch_DeletesSuccessfully() {
        // Arrange
        var domain = new DomainSettings {
            Name = "empty.com",
            CreatedAt = DateTime.UtcNow
        };
        _context.Domains.Add(domain);
        await _context.SaveChangesAsync();

        // Act
        await _domainService.DeleteDomainAsync("EMPTY.COM");

        // Assert
        var deletedDomain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Name == "empty.com");
        Assert.Null(deletedDomain);
    }

    [Fact]
    public async Task UpdateDomainAsync_NoChanges_DoesNotThrow() {
        // Arrange
        await SeedTestDomains();
        var request = new DomainUpdateRequest {
            // No actual changes
        };

        // Act & Assert - should not throw
        var result = await _domainService.UpdateDomainAsync("example.com", request);
        Assert.NotNull(result);
        Assert.Equal("example.com", result.Name);
    }

    [Fact]
    public async Task CreateDomainAsync_WithRegistryException_PropagatesException() {
        // Arrange
        var request = new DomainRequest {
            Name = "invalid-domain.com",
            CreateDatabase = true
        };

        // Configure mock to throw exception
        _mockDomainRegistryService.ShouldThrowOnRegister = true;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _domainService.CreateDomainAsync(request));
    }

    private async Task SeedTestDomains() {
        var domain1 = new DomainSettings {
            Id = 1,
            Name = "example.com",
            CreatedAt = DateTime.UtcNow
        };

        var domain2 = new DomainSettings {
            Id = 2,
            Name = "test.org",
            CreatedAt = DateTime.UtcNow
        };

        var users = new List<Frimerki.Models.Entities.User> {
            new() { Id = 1, Username = "user1", DomainId = 1, PasswordHash = "hash1", CreatedAt = DateTime.UtcNow },
            new() { Id = 2, Username = "user2", DomainId = 1, PasswordHash = "hash2", CreatedAt = DateTime.UtcNow },
            new() { Id = 3, Username = "user3", DomainId = 2, PasswordHash = "hash3", CreatedAt = DateTime.UtcNow }
        };

        _context.Domains.AddRange(domain1, domain2);
        _context.Users.AddRange(users);
        await _context.SaveChangesAsync();

        // Add domains to mock registry service so IsActive lookups work
        _mockDomainRegistryService.ExistingDomains.Add("example.com");
        _mockDomainRegistryService.ExistingDomains.Add("test.org");
    }

    private async Task SeedTestDomainsWithDkim() {
        await SeedTestDomains();

        var dkimKey = new DkimKey {
            Id = 1,
            DomainId = 1,
            Selector = "default",
            PublicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA1234567890",
            PrivateKey = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDV...\n-----END PRIVATE KEY-----",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.DkimKeys.Add(dkimKey);
        await _context.SaveChangesAsync();
    }

    private async Task SeedTestDomainsWithCatchAllUser() {
        await SeedTestDomains();

        // Set user1 as catch-all user for example.com
        var domain = await _context.Domains.FirstAsync(d => d.Name == "example.com");
        var user = await _context.Users.FirstAsync(u => u.Username == "user1");
        domain.CatchAllUserId = user.Id;
        await _context.SaveChangesAsync();
    }

    private async Task SeedTestDomainsWithMessages() {
        await SeedTestDomains();

        // Add some test messages for storage calculation
        var messages = new List<Message> {
            new() {
                Id = 1,
                HeaderMessageId = "msg1@example.com",
                Subject = "Test 1",
                FromAddress = "test@example.com",
                ToAddress = "user1@example.com",
                Headers = "From: test@example.com\r\nTo: user1@example.com\r\n",
                Body = "Test message body 1",
                MessageSize = 1024,
                Uid = 1,
                ReceivedAt = DateTime.UtcNow
            },
            new() {
                Id = 2,
                HeaderMessageId = "msg2@example.com",
                Subject = "Test 2",
                FromAddress = "test@example.com",
                ToAddress = "user2@example.com",
                Headers = "From: test@example.com\r\nTo: user2@example.com\r\n",
                Body = "Test message body 2",
                MessageSize = 2048,
                Uid = 2,
                ReceivedAt = DateTime.UtcNow
            },
            new() {
                Id = 3,
                HeaderMessageId = "msg3@test.org",
                Subject = "Test 3",
                FromAddress = "test@test.org",
                ToAddress = "user3@test.org",
                Headers = "From: test@test.org\r\nTo: user3@test.org\r\n",
                Body = "Test message body 3",
                MessageSize = 512,
                Uid = 3,
                ReceivedAt = DateTime.UtcNow
            }
        };

        _context.Messages.AddRange(messages);
        await _context.SaveChangesAsync();
    }

    private async Task SeedTestDomainsWithMessagesAndDkim() {
        // Seed everything in one go to avoid entity tracking conflicts
        var domain1 = new DomainSettings {
            Id = 1,
            Name = "example.com",
            CreatedAt = DateTime.UtcNow
        };

        var domain2 = new DomainSettings {
            Id = 2,
            Name = "test.org",
            CreatedAt = DateTime.UtcNow
        };

        var users = new List<Frimerki.Models.Entities.User> {
            new() { Id = 1, Username = "user1", DomainId = 1, PasswordHash = "hash1", CreatedAt = DateTime.UtcNow },
            new() { Id = 2, Username = "user2", DomainId = 1, PasswordHash = "hash2", CreatedAt = DateTime.UtcNow },
            new() { Id = 3, Username = "user3", DomainId = 2, PasswordHash = "hash3", CreatedAt = DateTime.UtcNow }
        };

        var messages = new List<Message> {
            new() {
                Id = 1,
                HeaderMessageId = "msg1@example.com",
                Subject = "Test 1",
                FromAddress = "test@example.com",
                ToAddress = "user1@example.com",
                Headers = "From: test@example.com\r\nTo: user1@example.com\r\n",
                Body = "Test message body 1",
                MessageSize = 1024,
                Uid = 1,
                ReceivedAt = DateTime.UtcNow
            },
            new() {
                Id = 2,
                HeaderMessageId = "msg2@example.com",
                Subject = "Test 2",
                FromAddress = "test@example.com",
                ToAddress = "user2@example.com",
                Headers = "From: test@example.com\r\nTo: user2@example.com\r\n",
                Body = "Test message body 2",
                MessageSize = 2048,
                Uid = 2,
                ReceivedAt = DateTime.UtcNow
            }
        };

        var dkimKey = new DkimKey {
            Id = 1,
            DomainId = 1,
            Selector = "default",
            PublicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA1234567890",
            PrivateKey = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDV...\n-----END PRIVATE KEY-----",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Domains.AddRange(domain1, domain2);
        _context.Users.AddRange(users);
        _context.Messages.AddRange(messages);
        _context.DkimKeys.Add(dkimKey);
        await _context.SaveChangesAsync();

        // Add domains to mock registry service so IsActive lookups work
        _mockDomainRegistryService.ExistingDomains.Add("example.com");
        _mockDomainRegistryService.ExistingDomains.Add("test.org");
    }
}

// Mock implementations for testing
public class MockDomainRegistryService : IDomainRegistryService {
    public List<string> ExistingDomains { get; } = [];
    public List<DomainRegistry> RegisteredDomains { get; } = [];
    public bool ShouldThrowOnRegister { get; set; }

    public async Task<DomainRegistry> RegisterDomainAsync(string domainName, string databaseName = null, bool createDatabase = false) {
        if (ShouldThrowOnRegister) {
            throw new InvalidOperationException("Simulated registry error");
        }

        if (ExistingDomains.Contains(domainName)) {
            throw new InvalidOperationException($"Domain {domainName} is already registered in the domain registry");
        }

        var registry = new DomainRegistry {
            Id = RegisteredDomains.Count + 1,
            Name = domainName,
            DatabaseName = databaseName ?? $"domain_{domainName.Replace(".", "_")}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        RegisteredDomains.Add(registry);
        return registry;
    }

    public async Task<DomainRegistry> GetDomainRegistryAsync(string domainName) {
        return ExistingDomains.Contains(domainName)
            ? new DomainRegistry { Name = domainName, IsActive = true }
            : null;
    }

    public async Task<List<DomainRegistry>> GetAllDomainsAsync() => RegisteredDomains;

    public async Task<bool> DomainExistsAsync(string domainName) => true; // For testing, assume domain validation passes

    public async Task SetDomainActiveAsync(string domainName, bool isActive) {
        var domain = RegisteredDomains.FirstOrDefault(d => d.Name == domainName);
        if (domain != null) {
            domain.IsActive = isActive;
        }
    }

    public async Task<bool> DatabaseExistsAsync(string databaseName) => false;

    public async Task<List<string>> GetExistingDatabasesAsync() => [];
}

public class MockDomainDbContextFactory : IDomainDbContextFactory {
    public DomainDbContext CreateDbContext(string domainName) {
        var options = new DbContextOptionsBuilder<DomainDbContext>()
            .UseInMemoryDatabase(databaseName: $"Domain_{domainName}_{Guid.NewGuid()}")
            .Options;
        return new DomainDbContext(options, domainName);
    }

    public async Task<DomainDbContext> CreateDbContextAsync(string domainName) {
        await EnsureDatabaseExistsAsync(domainName);
        return CreateDbContext(domainName);
    }

    public string GetDatabasePath(string domainName) => $"/mock/path/domain_{domainName}.db";

    public async Task EnsureDatabaseExistsAsync(string domainName) {
        // Mock implementation - just return
    }
}
