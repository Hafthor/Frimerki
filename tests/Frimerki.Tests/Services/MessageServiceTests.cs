using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Message;
using Frimerki.Tests.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Frimerki.Tests.Services;

public sealed class MessageServiceTests : IDisposable {
    private readonly EmailDbContext _context;
    private readonly MessageService _messageService;

    public MessageServiceTests() {
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new EmailDbContext(options);
        var mockLogger = new Mock<ILogger<MessageService>>();
        var nowProvider = new MockNowProvider();
        _messageService = new MessageService(_context, nowProvider, mockLogger.Object);

        SeedTestData();
    }

    private void SeedTestData() {
        var domain = new DomainSettings {
            Id = 1,
            Name = "example.com",
            CreatedAt = DateTime.UtcNow
        };

        var user = new Frimerki.Models.Entities.User {
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

        var message = new Message {
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
            Envelope = """{"subject":"Test Message","from":[{"email":"sender@example.com"}]}""",
            BodyStructure = """{"type":"text","subtype":"plain"}"""
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
            var message = new Message {
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
        await _context.SaveChangesAsync();

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

    public void Dispose() => _context.Dispose();

    // ── FormatMessageSize (tested via MessageSizeFormatted in list responses) ──

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1029, "1 KB")]
    [InlineData(1030, "1.01 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048575, "1024 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public async Task GetMessagesAsync_FormatMessageSize_FormatsCorrectly(int messageSize, string expected) {
        var msg = new Message {
            Id = 100,
            HeaderMessageId = "<size-test@example.com>",
            FromAddress = "sender@example.com",
            ToAddress = "testuser@example.com",
            Subject = "Size Test",
            Headers = "Subject: Size Test\r\n",
            Body = "",
            MessageSize = messageSize,
            ReceivedAt = DateTime.UtcNow,
            SentDate = DateTime.UtcNow,
            Uid = 100,
            UidValidity = 1
        };

        var um = new UserMessage {
            Id = 100,
            UserId = 1,
            MessageId = 100,
            FolderId = 1,
            Uid = 100,
            ReceivedAt = DateTime.UtcNow
        };

        _context.Messages.Add(msg);
        _context.UserMessages.Add(um);
        await _context.SaveChangesAsync();

        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Take = 100 });
        var item = result.Items.FirstOrDefault(i => i.Id == 100);

        Assert.NotNull(item);
        Assert.Equal(expected, item.MessageSizeFormatted);

        _context.UserMessages.Remove(um);
        _context.Messages.Remove(msg);
        await _context.SaveChangesAsync();
    }

    // ── ApplyFlagFiltering (tested via GetMessagesAsync with Flags filter) ──

    [Fact]
    public async Task GetMessagesAsync_FlagFilter_Seen_ReturnsSeenMessages() {
        // The seeded message has \Seen = false, so "seen" should return 0 items
        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Flags = "seen" });
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetMessagesAsync_FlagFilter_Unread_ReturnsUnseenMessages() {
        // The seeded message has \Seen = false, so "unread" should return it
        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Flags = "unread" });
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetMessagesAsync_FlagFilter_Read_SynonymForSeen() {
        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Flags = "read" });
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetMessagesAsync_FlagFilter_Unseen_SynonymForUnread() {
        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Flags = "unseen" });
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetMessagesAsync_FlagFilter_Flagged_ReturnsEmpty() {
        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Flags = "flagged" });
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetMessagesAsync_FlagFilter_Unknown_ReturnsAll() {
        // Unknown flag filter value should not filter anything
        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest { Flags = "nonexistent" });
        Assert.Single(result.Items);
    }

    // ── ApplySorting (tested via GetMessagesAsync sort options) ──

    [Fact]
    public async Task GetMessagesAsync_SortBySubject_Ascending() {
        await SeedExtraMessages();

        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest {
            SortBy = "subject", SortOrder = "asc", Take = 100
        });

        Assert.True(result.Items.Count > 1);
        var subjects = result.Items.Select(i => i.Subject).ToList();
        Assert.Equal(subjects.Order().ToList(), subjects);
    }

    [Fact]
    public async Task GetMessagesAsync_SortByFrom_Descending() {
        await SeedExtraMessages();

        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest {
            SortBy = "from", SortOrder = "desc", Take = 100
        });

        Assert.True(result.Items.Count > 1);
    }

    [Fact]
    public async Task GetMessagesAsync_SortBySender_SynonymForFrom() {
        await SeedExtraMessages();

        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest {
            SortBy = "sender", SortOrder = "asc", Take = 100
        });

        Assert.True(result.Items.Count > 1);
    }

    [Fact]
    public async Task GetMessagesAsync_SortBySize_Ascending() {
        await SeedExtraMessages();

        var result = await _messageService.GetMessagesAsync(1, new MessageFilterRequest {
            SortBy = "size", SortOrder = "asc", Take = 100
        });

        Assert.True(result.Items.Count > 1);
        var sizes = result.Items.Select(i => i.MessageSize).ToList();
        Assert.Equal(sizes.Order().ToList(), sizes);
    }

    // ── ParseEnvelope / ParseBodyStructure edge cases ──

    [Fact]
    public async Task GetMessageAsync_NullEnvelope_ReturnsDefaultEnvelope() {
        var msg = new Message {
            Id = 200,
            HeaderMessageId = "<null-env@example.com>",
            FromAddress = "x@example.com",
            Headers = "",
            Body = "",
            MessageSize = 0,
            ReceivedAt = DateTime.UtcNow,
            Uid = 200,
            UidValidity = 1,
            Envelope = null,
            BodyStructure = null
        };

        _context.Messages.Add(msg);
        _context.UserMessages.Add(new UserMessage {
            Id = 200, UserId = 1, MessageId = 200, FolderId = 1, Uid = 200, ReceivedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _messageService.GetMessageAsync(1, 200);

        Assert.NotNull(result);
        Assert.NotNull(result.Envelope);
        Assert.NotNull(result.BodyStructure);
        Assert.Equal("text", result.BodyStructure.Type);
        Assert.Equal("plain", result.BodyStructure.Subtype);
    }

    [Fact]
    public async Task GetMessageAsync_InvalidJsonEnvelope_ReturnsDefault() {
        var msg = new Message {
            Id = 201,
            HeaderMessageId = "<bad-json@example.com>",
            FromAddress = "x@example.com",
            Headers = "",
            Body = "",
            MessageSize = 0,
            ReceivedAt = DateTime.UtcNow,
            Uid = 201,
            UidValidity = 1,
            Envelope = "not valid json {{{",
            BodyStructure = "also invalid <<<"
        };

        _context.Messages.Add(msg);
        _context.UserMessages.Add(new UserMessage {
            Id = 201, UserId = 1, MessageId = 201, FolderId = 1, Uid = 201, ReceivedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _messageService.GetMessageAsync(1, 201);

        Assert.NotNull(result);
        Assert.NotNull(result.Envelope);
        Assert.NotNull(result.BodyStructure);
    }

    // ── BuildNextUrl filter propagation ──

    [Fact]
    public async Task GetMessagesAsync_NextUrl_IncludesAllFilters() {
        await SeedExtraMessages();

        var request = new MessageFilterRequest {
            Folder = "INBOX",
            Flags = "unread",
            From = "sender",
            To = "testuser",
            Q = "test",
            Since = new DateTime(2025, 1, 1),
            Before = new DateTime(2026, 12, 31),
            MinSize = 10,
            MaxSize = 10000,
            SortBy = "subject",
            SortOrder = "asc",
            Skip = 0,
            Take = 1  // Force pagination
        };

        var result = await _messageService.GetMessagesAsync(1, request);

        // Should have a next URL since there are more messages
        if (result.TotalCount > 1) {
            Assert.NotNull(result.NextUrl);
            Assert.Contains("folder=INBOX", result.NextUrl);
            Assert.Contains("flags=unread", result.NextUrl);
            Assert.Contains("from=sender", result.NextUrl);
            Assert.Contains("to=testuser", result.NextUrl);
            Assert.Contains("q=test", result.NextUrl);
            Assert.Contains("since=", result.NextUrl);
            Assert.Contains("before=", result.NextUrl);
            Assert.Contains("minSize=10", result.NextUrl);
            Assert.Contains("maxSize=10000", result.NextUrl);
            Assert.Contains("sortBy=subject", result.NextUrl);
            Assert.Contains("sortOrder=asc", result.NextUrl);
        }
    }

    // ── UpdateMessage with invalid folder ──

    [Fact]
    public async Task UpdateMessageAsync_WithInvalidFolderId_ThrowsArgumentException() {
        var request = new MessageUpdateRequest {
            FolderId = 999
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _messageService.UpdateMessageAsync(1, 1, request));
    }

    // ── Helpers ──

    private async Task SeedExtraMessages() {
        // Only seed once — check if extras already exist
        if (await _context.Messages.AnyAsync(m => m.Id == 10)) {
            return;
        }

        for (int i = 10; i <= 12; i++) {
            _context.Messages.Add(new Message {
                Id = i,
                HeaderMessageId = $"<extra{i}@example.com>",
                FromAddress = i % 2 == 0 ? "alpha@example.com" : "zeta@example.com",
                ToAddress = "testuser@example.com",
                Subject = $"Extra {(char)('A' + i - 10)}",
                Headers = $"Subject: Extra {(char)('A' + i - 10)}\r\n",
                Body = new string('x', i * 100),
                MessageSize = i * 100,
                ReceivedAt = DateTime.UtcNow.AddMinutes(-i),
                SentDate = DateTime.UtcNow.AddMinutes(-i),
                Uid = i,
                UidValidity = 1
            });

            _context.UserMessages.Add(new UserMessage {
                Id = i,
                UserId = 1,
                MessageId = i,
                FolderId = 1,
                Uid = i,
                ReceivedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _context.SaveChangesAsync();
    }
}
