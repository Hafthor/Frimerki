using Frimerki.Data;
using Frimerki.Models.DTOs.Folder;
using Frimerki.Models.Entities;
using Frimerki.Services.Folder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Frimerki.Tests.Services;

public class FolderServiceTests : IDisposable {
    private readonly EmailDbContext _context;
    private readonly FolderService _folderService;
    private readonly Mock<ILogger<FolderService>> _mockLogger;

    public FolderServiceTests() {
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new EmailDbContext(options);
        _mockLogger = new Mock<ILogger<FolderService>>();
        _folderService = new FolderService(_context, _mockLogger.Object);

        SeedTestData();
    }

    private void SeedTestData() {
        var domain = new Domain {
            Id = 1,
            Name = "example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User {
            Id = 1,
            Username = "testuser",
            DomainId = 1,
            PasswordHash = "hashedpassword",
            Salt = "salt",
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
                Subscribed = true,
                User = user
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
                Subscribed = true,
                User = user
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
                Subscribed = false,
                User = user
            }
        };

        _context.Domains.Add(domain);
        _context.Users.Add(user);
        _context.Folders.AddRange(folders);
        _context.SaveChanges();
    }

    [Fact]
    public async Task GetFoldersAsync_ReturnsAllUserFolders() {
        // Act
        var result = await _folderService.GetFoldersAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);

        var inbox = result.First(f => f.Name == "INBOX");
        Assert.Equal("INBOX", inbox.SystemFolderType);
        Assert.Equal(5, inbox.MessageCount);
        Assert.Equal(2, inbox.UnseenCount);
        Assert.True(inbox.Subscribed);

        var work = result.First(f => f.Name == "INBOX/Work");
        Assert.Null(work.SystemFolderType);
        Assert.Equal(2, work.MessageCount);
        Assert.Equal(1, work.UnseenCount);
        Assert.False(work.Subscribed);
    }

    [Fact]
    public async Task GetFolderAsync_WithValidName_ReturnsFolder() {
        // Act
        var result = await _folderService.GetFolderAsync(1, "INBOX");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INBOX", result.Name);
        Assert.Equal("INBOX", result.SystemFolderType);
        Assert.Equal(5, result.Exists);
        Assert.Equal(1, result.Recent);
        Assert.Equal(2, result.Unseen);
        Assert.Equal(1, result.UidNext);
        Assert.Equal(1, result.UidValidity);
        Assert.True(result.Subscribed);
    }

    [Fact]
    public async Task GetFolderAsync_WithEncodedName_ReturnsFolder() {
        // Act - Test URL-encoded folder name
        var result = await _folderService.GetFolderAsync(1, "INBOX%2FWork");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INBOX/Work", result.Name);
        Assert.Null(result.SystemFolderType);
        Assert.False(result.Subscribed);
    }

    [Fact]
    public async Task GetFolderAsync_WithInvalidName_ReturnsNull() {
        // Act
        var result = await _folderService.GetFolderAsync(1, "NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFolderAsync_WithWrongUser_ReturnsNull() {
        // Act
        var result = await _folderService.GetFolderAsync(999, "INBOX");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateFolderAsync_WithValidRequest_CreatesFolder() {
        // Arrange
        var request = new FolderRequest {
            Name = "INBOX/Projects",
            Subscribed = true
        };

        // Act
        var result = await _folderService.CreateFolderAsync(1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INBOX/Projects", result.Name);
        Assert.Null(result.SystemFolderType);
        Assert.True(result.Subscribed);
        Assert.Equal(1, result.UidNext);
        Assert.True(result.UidValidity > 0); // UidValidity is generated, just check it's positive
    }

    [Fact]
    public async Task CreateFolderAsync_WithExistingName_ThrowsException() {
        // Arrange
        var request = new FolderRequest {
            Name = "INBOX"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _folderService.CreateFolderAsync(1, request)
        );
    }

    [Fact]
    public async Task CreateFolderAsync_WithInvalidParent_ThrowsException() {
        // Arrange
        var request = new FolderRequest {
            Name = "NonExistent/SubFolder"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _folderService.CreateFolderAsync(1, request)
        );
    }

    [Fact]
    public async Task UpdateFolderAsync_WithValidRequest_UpdatesFolder() {
        // Arrange
        var request = new FolderUpdateRequest {
            Name = "INBOX/Business",
            Subscribed = true
        };

        // Act
        var result = await _folderService.UpdateFolderAsync(1, "INBOX/Work", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INBOX/Business", result.Name);
        Assert.True(result.Subscribed);
    }

    [Fact]
    public async Task UpdateFolderAsync_WithSystemFolder_ThrowsException() {
        // Arrange
        var request = new FolderUpdateRequest {
            Name = "MyInbox"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _folderService.UpdateFolderAsync(1, "INBOX", request)
        );
    }

    [Fact]
    public async Task UpdateFolderAsync_WithInvalidName_ReturnsNull() {
        // Arrange
        var request = new FolderUpdateRequest {
            Name = "NewName"
        };

        // Act
        var result = await _folderService.UpdateFolderAsync(1, "NonExistent", request);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteFolderAsync_WithValidName_DeletesFolder() {
        // Act
        var result = await _folderService.DeleteFolderAsync(1, "INBOX/Work");

        // Assert
        Assert.True(result);

        // Verify folder is deleted
        var folder = await _folderService.GetFolderAsync(1, "INBOX/Work");
        Assert.Null(folder);
    }

    [Fact]
    public async Task DeleteFolderAsync_WithSystemFolder_ThrowsException() {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _folderService.DeleteFolderAsync(1, "INBOX")
        );
    }

    [Fact]
    public async Task DeleteFolderAsync_WithMessages_ThrowsException() {
        // Arrange - Add a message to the folder
        var message = new Frimerki.Models.Entities.Message {
            Id = 1,
            HeaderMessageId = "<test@example.com>",
            FromAddress = "sender@example.com",
            ToAddress = "testuser@example.com",
            Subject = "Test",
            Headers = "Test",
            MessageSize = 100,
            Uid = 1,
            UidValidity = 1
        };

        var userMessage = new UserMessage {
            Id = 1,
            UserId = 1,
            MessageId = 1,
            FolderId = 3, // INBOX/Work folder
            Uid = 1
        };

        _context.Messages.Add(message);
        _context.UserMessages.Add(userMessage);
        _context.SaveChanges();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _folderService.DeleteFolderAsync(1, "INBOX/Work")
        );
    }

    [Fact]
    public async Task DeleteFolderAsync_WithInvalidName_ReturnsFalse() {
        // Act
        var result = await _folderService.DeleteFolderAsync(1, "NonExistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteFolderAsync_WithWrongUser_ReturnsFalse() {
        // Act
        var result = await _folderService.DeleteFolderAsync(999, "INBOX/Work");

        // Assert
        Assert.False(result);
    }

    public void Dispose() {
        _context.Dispose();
    }
}
