using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Common;
using Frimerki.Services.Message;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Frimerki.Tests.Services;

public class MessageServiceTests : IDisposable {
    private readonly EmailDbContext _context;
    private readonly MessageService _messageService;
    private readonly Mock<ILogger<MessageService>> _mockLogger;
    private readonly TestNowProvider _nowProvider;

    public MessageServiceTests() {
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new EmailDbContext(options);
        _mockLogger = new Mock<ILogger<MessageService>>();
        _nowProvider = new TestNowProvider();
        _messageService = new MessageService(_context, _nowProvider, _mockLogger.Object);

        SeedTestData();
    }

    private void SeedTestData() {
        var domain = new DomainSettings {
            Id = 1,
            Name = "example.com",
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

        var inboxFolder = new Folder {
            Id = 1,
            UserId = 1,
            Name = "INBOX",
            SystemFolderType = "INBOX",
            UidNext = 1,
            UidValidity = 1,
            Exists = 0,
            Recent = 0,
            Unseen = 0,
            Subscribed = true,
            User = user
        };

        var sentFolder = new Folder {
            Id = 2,
            UserId = 1,
            Name = "SENT",
            SystemFolderType = "SENT",
            UidNext = 1,
            UidValidity = 1,
            Exists = 0,
            Recent = 0,
            Unseen = 0,
            Subscribed = true,
            User = user
        };

        var trashFolder = new Folder {
            Id = 3,
            UserId = 1,
            Name = "TRASH",
            SystemFolderType = "TRASH",
            UidNext = 1,
            UidValidity = 1,
            Exists = 0,
            Recent = 0,
            Unseen = 0,
            Subscribed = true,
            User = user
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
            ReceivedAt = DateTime.UtcNow,
            User = user,
            Message = message,
            Folder = inboxFolder
        };

        var messageFlag = new MessageFlag {
            Id = 1,
            MessageId = 1,
            UserId = 1,
            FlagName = "\\Seen",
            IsSet = false,
            Message = message,
            User = user
        };

        _context.Domains.Add(domain);
        _context.Users.Add(user);
        _context.Folders.AddRange(inboxFolder, sentFolder, trashFolder);
        _context.Messages.Add(message);
        _context.UserMessages.Add(userMessage);
        _context.MessageFlags.Add(messageFlag);
        _context.SaveChanges();
    }

    [Fact]
    public async Task GetMessagesAsync_WithNoFilters_ReturnsAllUserMessages() {
        // Arrange
        var request = new MessageFilterRequest {
            Skip = 0,
            Take = 50
        };

        // Act
        var result = await _messageService.GetMessagesAsync(1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Test Message", result.Items[0].Subject);
        Assert.Equal("sender@example.com", result.Items[0].FromAddress);
        Assert.Null(result.NextUrl); // No more pages
    }

    [Fact]
    public async Task GetMessagesAsync_WithFolderFilter_ReturnsFilteredMessages() {
        // Arrange
        var request = new MessageFilterRequest {
            Folder = "INBOX",
            Skip = 0,
            Take = 50
        };

        // Act
        var result = await _messageService.GetMessagesAsync(1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("INBOX", result.Items[0].Folder);
        Assert.Contains("folder", result.AppliedFilters!.Keys);
        Assert.Equal("INBOX", result.AppliedFilters["folder"]);
    }

    [Fact]
    public async Task GetMessagesAsync_WithSearchQuery_ReturnsMatchingMessages() {
        // Arrange
        var request = new MessageFilterRequest {
            Q = "test",
            Skip = 0,
            Take = 50
        };

        // Act
        var result = await _messageService.GetMessagesAsync(1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Contains("q", result.AppliedFilters!.Keys);
        Assert.Equal("test", result.AppliedFilters["q"]);
    }

    [Fact]
    public async Task GetMessagesAsync_WithPagination_ReturnsCorrectPage() {
        // Arrange
        // Add more test messages to test pagination
        for (int i = 2; i <= 55; i++) {
            var message = new Frimerki.Models.Entities.Message {
                Id = i,
                HeaderMessageId = $"<test{i}@example.com>",
                FromAddress = "sender@example.com",
                ToAddress = "testuser@example.com",
                Subject = $"Test Message {i}",
                Headers = $"From: sender@example.com\r\nTo: testuser@example.com\r\nSubject: Test Message {i}\r\n",
                Body = $"This is test message {i}.",
                MessageSize = 100,
                ReceivedAt = DateTime.UtcNow,
                SentDate = DateTime.UtcNow,
                Uid = i,
                UidValidity = 1
            };

            var userMessage = new UserMessage {
                Id = i,
                UserId = 1,
                MessageId = i,
                FolderId = 1,
                Uid = i,
                ReceivedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            _context.UserMessages.Add(userMessage);
        }
        _context.SaveChanges();

        var request = new MessageFilterRequest {
            Skip = 0,
            Take = 25
        };

        // Act
        var result = await _messageService.GetMessagesAsync(1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(25, result.Items.Count);
        Assert.Equal(55, result.TotalCount);
        Assert.Equal(0, result.Skip);
        Assert.Equal(25, result.Take);
        Assert.NotNull(result.NextUrl);
        Assert.Contains("skip=25", result.NextUrl);
    }

    [Fact]
    public async Task GetMessageAsync_WithValidId_ReturnsMessage() {
        // Act
        var result = await _messageService.GetMessageAsync(1, 1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Test Message", result.Subject);
        Assert.Equal("sender@example.com", result.FromAddress);
        Assert.Equal("testuser@example.com", result.ToAddress);
        Assert.NotNull(result.Envelope);
        Assert.NotNull(result.BodyStructure);
        Assert.NotNull(result.Flags);
        Assert.False(result.Flags.Seen); // Should be false based on test data
    }

    [Fact]
    public async Task GetMessageAsync_WithInvalidId_ReturnsNull() {
        // Act
        var result = await _messageService.GetMessageAsync(1, 999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMessageAsync_WithWrongUser_ReturnsNull() {
        // Act
        var result = await _messageService.GetMessageAsync(999, 1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMessageAsync_WithValidRequest_CreatesMessage() {
        // Arrange
        var request = new MessageRequest {
            ToAddress = "recipient@example.com",
            Subject = "New Test Message",
            Body = "This is a new test message."
        };

        // Act
        var result = await _messageService.CreateMessageAsync(1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("New Test Message", result.Subject);
        Assert.Equal("recipient@example.com", result.ToAddress);
        Assert.Equal("SENT", result.Folder);
        Assert.True(result.Flags.Seen); // Should be marked as seen since user is sending
    }

    [Fact]
    public async Task UpdateMessageAsync_WithValidFlags_UpdatesFlags() {
        // Arrange
        var request = new MessageUpdateRequest {
            Flags = new MessageFlagsRequest {
                Seen = true,
                Flagged = true
            }
        };

        // Act
        var result = await _messageService.UpdateMessageAsync(1, 1, request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Flags.Seen);
        Assert.True(result.Flags.Flagged);
    }

    [Fact]
    public async Task UpdateMessageAsync_WithFolderMove_MovesMessage() {
        // Arrange
        var request = new MessageUpdateRequest {
            FolderId = 2 // Move to SENT folder
        };

        // Act
        var result = await _messageService.UpdateMessageAsync(1, 1, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("SENT", result.Folder);
    }

    [Fact]
    public async Task UpdateMessageAsync_WithInvalidId_ReturnsNull() {
        // Arrange
        var request = new MessageUpdateRequest {
            Flags = new MessageFlagsRequest { Seen = true }
        };

        // Act
        var result = await _messageService.UpdateMessageAsync(1, 999, request);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteMessageAsync_WithValidId_MovesToTrash() {
        // Act
        var result = await _messageService.DeleteMessageAsync(1, 1);

        // Assert
        Assert.True(result);

        // Verify message is moved to trash
        var message = await _messageService.GetMessageAsync(1, 1);
        Assert.NotNull(message);
        Assert.Equal("TRASH", message.Folder);
        Assert.True(message.Flags.Deleted);
    }

    [Fact]
    public async Task DeleteMessageAsync_WithInvalidId_ReturnsFalse() {
        // Act
        var result = await _messageService.DeleteMessageAsync(1, 999);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteMessageAsync_WithWrongUser_ReturnsFalse() {
        // Act
        var result = await _messageService.DeleteMessageAsync(999, 1);

        // Assert
        Assert.False(result);
    }

    public void Dispose() {
        _context.Dispose();
    }
}
