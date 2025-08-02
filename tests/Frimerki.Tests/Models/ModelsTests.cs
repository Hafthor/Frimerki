using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;

namespace Frimerki.Tests.Models;

/// <summary>
/// Tests for DTOs and entities to improve code coverage.
/// These are mainly property verification tests since DTOs are data containers.
/// </summary>
public class ModelsTests {

    [Fact]
    public void CreateDomainResponse_CanSetAndGetProperties() {
        // Arrange & Act
        var response = new CreateDomainResponse {
            Name = "test.com",
            DatabaseName = "test_db",
            IsActive = true,
            IsDedicated = false,
            CreatedAt = DateTime.UtcNow,
            DatabaseCreated = true,
            InitialSetup = new InitialSetupInfo {
                AdminUserCreated = true,
                DefaultFoldersCreated = true,
                DkimKeysGenerated = false
            }
        };

        // Assert
        Assert.Equal("test.com", response.Name);
        Assert.Equal("test_db", response.DatabaseName);
        Assert.True(response.IsActive);
        Assert.False(response.IsDedicated);
        Assert.True(response.DatabaseCreated);
        Assert.NotNull(response.InitialSetup);
        Assert.True(response.InitialSetup.AdminUserCreated);
    }

    [Fact]
    public void DatabaseInfo_CanSetAndGetProperties() {
        // Arrange & Act
        var info = new DatabaseInfo {
            Name = "domain_test",
            FilePath = "/path/to/db",
            FileSize = 1024000,
            IsDedicated = true,
            Domains = ["example.com", "test.org"],
            TotalUsers = 25,
            TotalMessages = 1000
        };

        // Assert
        Assert.Equal("domain_test", info.Name);
        Assert.Equal("/path/to/db", info.FilePath);
        Assert.Equal(1024000, info.FileSize);
        Assert.True(info.IsDedicated);
        Assert.Equal(2, info.Domains.Count);
        Assert.Equal(25, info.TotalUsers);
        Assert.Equal(1000, info.TotalMessages);
    }

    [Fact]
    public void DatabaseListResponse_CanSetAndGetProperties() {
        // Arrange & Act
        var response = new DatabaseListResponse {
            Databases = [
                new DatabaseInfo { Name = "db1", FileSize = 1000 },
                new DatabaseInfo { Name = "db2", FileSize = 2000 }
            ],
            TotalDomains = 2,
            TotalSize = 3000
        };

        // Assert
        Assert.Equal(2, response.Databases.Count);
        Assert.Equal(2, response.TotalDomains);
        Assert.Equal(3000, response.TotalSize);
        Assert.Equal("db1", response.Databases[0].Name);
    }

    [Fact]
    public void DkimKeyInfo_CanSetAndGetProperties() {
        // Arrange & Act
        var keyInfo = new DkimKeyInfo {
            Selector = "default",
            PublicKey = "test-public-key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("default", keyInfo.Selector);
        Assert.Equal("test-public-key", keyInfo.PublicKey);
        Assert.True(keyInfo.IsActive);
        Assert.True(keyInfo.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void DkimKeyResponse_CanSetAndGetProperties() {
        // Arrange & Act
        var response = new DkimKeyResponse {
            Selector = "mail",
            PublicKey = "public-key-data",
            DnsRecord = "v=DKIM1; k=rsa; p=public-key-data",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("mail", response.Selector);
        Assert.Equal("public-key-data", response.PublicKey);
        Assert.StartsWith("v=DKIM1", response.DnsRecord);
        Assert.True(response.IsActive);
    }

    [Fact]
    public void DomainListResponse_CanSetAndGetProperties() {
        // Arrange & Act
        var response = new DomainListResponse {
            Domains = [
                new DomainResponse { Name = "example.com", IsActive = true },
                new DomainResponse { Name = "test.org", IsActive = false }
            ],
            TotalCount = 2
        };

        // Assert
        Assert.Equal(2, response.Domains.Count);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal("example.com", response.Domains[0].Name);
        Assert.True(response.Domains[0].IsActive);
        Assert.False(response.Domains[1].IsActive);
    }

    [Fact]
    public void DomainRequest_CanSetAndGetProperties() {
        // Arrange & Act
        var request = new DomainRequest {
            Name = "newdomain.com",
            DatabaseName = "custom_db",
            CreateDatabase = true,
            CatchAllUser = "admin@newdomain.com"
        };

        // Assert
        Assert.Equal("newdomain.com", request.Name);
        Assert.Equal("custom_db", request.DatabaseName);
        Assert.True(request.CreateDatabase);
        Assert.Equal("admin@newdomain.com", request.CatchAllUser);
    }

    [Fact]
    public void GenerateDkimKeyRequest_CanSetAndGetProperties() {
        // Arrange & Act
        var request = new GenerateDkimKeyRequest {
            Selector = "default",
            KeySize = 2048
        };

        // Assert
        Assert.Equal("default", request.Selector);
        Assert.Equal(2048, request.KeySize);
    }

    [Fact]
    public void InitialSetupInfo_CanSetAndGetProperties() {
        // Arrange & Act
        var setupInfo = new InitialSetupInfo {
            AdminUserCreated = true,
            DefaultFoldersCreated = true,
            DkimKeysGenerated = false
        };

        // Assert
        Assert.True(setupInfo.AdminUserCreated);
        Assert.True(setupInfo.DefaultFoldersCreated);
        Assert.False(setupInfo.DkimKeysGenerated);
    }

    [Fact]
    public void MessageAttachmentResponse_CanSetAndGetProperties() {
        // Arrange & Act
        var response = new MessageAttachmentResponse {
            FileName = "document.pdf",
            ContentType = "application/pdf",
            Size = 1024,
            SizeFormatted = "1.0 KB",
            Path = "/attachments/doc.pdf"
        };

        // Assert
        Assert.Equal("document.pdf", response.FileName);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.Equal(1024, response.Size);
        Assert.Equal("1.0 KB", response.SizeFormatted);
        Assert.Equal("/attachments/doc.pdf", response.Path);
    }

    [Fact]
    public void RestoreRequest_CanSetAndGetProperties() {
        // Arrange & Act
        var request = new RestoreRequest {
            BackupId = "backup-123",
            RestoreSettings = true,
            RestoreUsers = true,
            RestoreMessages = false,
            RestoreAttachments = true
        };

        // Assert
        Assert.Equal("backup-123", request.BackupId);
        Assert.True(request.RestoreSettings);
        Assert.True(request.RestoreUsers);
        Assert.False(request.RestoreMessages);
        Assert.True(request.RestoreAttachments);
    }

    [Fact]
    public void RestoreResponse_CanSetAndGetProperties() {
        // Arrange & Act
        var response = new RestoreResponse {
            Status = "Completed",
            Message = "Restore completed successfully",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("Completed", response.Status);
        Assert.Equal("Restore completed successfully", response.Message);
        Assert.True(response.StartedAt < DateTime.UtcNow);
    }

    [Fact]
    public void ServerLogEntry_CanSetAndGetProperties() {
        // Arrange & Act
        var entry = new ServerLogEntry {
            Timestamp = DateTime.UtcNow,
            Level = "INFO",
            Message = "Test log message",
            Logger = "TestService",
            Exception = null
        };

        // Assert
        Assert.Equal("INFO", entry.Level);
        Assert.Equal("Test log message", entry.Message);
        Assert.Equal("TestService", entry.Logger);
        Assert.Null(entry.Exception);
        Assert.True(entry.Timestamp > DateTime.MinValue);
    }

    [Fact]
    public void UserPasswordUpdateRequest_CanSetAndGetProperties() {
        // Arrange & Act
        var request = new UserPasswordUpdateRequest {
            CurrentPassword = "oldpassword",
            NewPassword = "newpassword123"
        };

        // Assert
        Assert.Equal("oldpassword", request.CurrentPassword);
        Assert.Equal("newpassword123", request.NewPassword);
    }

    [Fact]
    public void UserRequest_CanSetAndGetProperties() {
        // Arrange & Act
        var request = new UserRequest {
            Username = "testuser",
            Email = "testuser@example.com",
            Password = "password123",
            FullName = "Test User",
            Role = "User",
            CanReceive = true,
            CanLogin = true
        };

        // Assert
        Assert.Equal("testuser", request.Username);
        Assert.Equal("testuser@example.com", request.Email);
        Assert.Equal("password123", request.Password);
        Assert.Equal("Test User", request.FullName);
        Assert.Equal("User", request.Role);
        Assert.True(request.CanReceive);
        Assert.True(request.CanLogin);
    }

    [Fact]
    public void UserUpdateRequest_CanSetAndGetProperties() {
        // Arrange & Act
        var request = new UserUpdateRequest {
            FullName = "Updated Name",
            Role = "DomainAdmin",
            CanReceive = false,
            CanLogin = true
        };

        // Assert
        Assert.Equal("Updated Name", request.FullName);
        Assert.Equal("DomainAdmin", request.Role);
        Assert.False(request.CanReceive);
        Assert.True(request.CanLogin);
    }

    [Fact]
    public void Attachment_Entity_CanSetAndGetProperties() {
        // Arrange & Act
        var attachment = new Attachment {
            Id = 1,
            MessageId = 100,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 256,
            FileGuid = "550e8400-e29b-41d4-a716-446655440000",
            FileExtension = ".txt",
            FilePath = "/attachments/550e8400-e29b-41d4-a716-446655440000.txt"
        };

        // Assert
        Assert.Equal(1, attachment.Id);
        Assert.Equal(100, attachment.MessageId);
        Assert.Equal("test.txt", attachment.FileName);
        Assert.Equal("text/plain", attachment.ContentType);
        Assert.Equal(256, attachment.Size);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", attachment.FileGuid);
        Assert.Equal(".txt", attachment.FileExtension);
        Assert.Equal("/attachments/550e8400-e29b-41d4-a716-446655440000.txt", attachment.FilePath);
    }

    [Fact]
    public void DkimKey_Entity_CanSetAndGetProperties() {
        // Arrange & Act
        var dkimKey = new DkimKey {
            Id = 1,
            DomainId = 10,
            Selector = "default",
            PublicKey = "public-key",
            PrivateKey = "private-key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(1, dkimKey.Id);
        Assert.Equal(10, dkimKey.DomainId);
        Assert.Equal("default", dkimKey.Selector);
        Assert.Equal("public-key", dkimKey.PublicKey);
        Assert.Equal("private-key", dkimKey.PrivateKey);
        Assert.True(dkimKey.IsActive);
    }

    [Fact]
    public void HostAdmin_Entity_CanSetAndGetProperties() {
        // Arrange & Act
        var hostAdmin = new HostAdmin {
            Id = 1,
            Username = "admin",
            PasswordHash = "hashed-password",
            Email = "admin@localhost",
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(1, hostAdmin.Id);
        Assert.Equal("admin", hostAdmin.Username);
        Assert.Equal("hashed-password", hostAdmin.PasswordHash);
        Assert.Equal("admin@localhost", hostAdmin.Email);
        Assert.True(hostAdmin.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void ServerConfiguration_Entity_CanSetAndGetProperties() {
        // Arrange & Act
        var config = new ServerConfiguration {
            Id = 1,
            Key = "smtp.enabled",
            Value = "true",
            Description = "Enable SMTP server",
            ModifiedAt = DateTime.UtcNow,
            ModifiedBy = "admin"
        };

        // Assert
        Assert.Equal(1, config.Id);
        Assert.Equal("smtp.enabled", config.Key);
        Assert.Equal("true", config.Value);
        Assert.Equal("Enable SMTP server", config.Description);
        Assert.True(config.ModifiedAt > DateTime.MinValue);
        Assert.Equal("admin", config.ModifiedBy);
    }

    [Fact]
    public void UidValiditySequence_Entity_CanSetAndGetProperties() {
        // Arrange & Act
        var sequence = new UidValiditySequence {
            Id = 1,
            DomainId = 10,
            Value = 12345,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(1, sequence.Id);
        Assert.Equal(10, sequence.DomainId);
        Assert.Equal(12345, sequence.Value);
        Assert.True(sequence.CreatedAt > DateTime.MinValue);
    }
}
