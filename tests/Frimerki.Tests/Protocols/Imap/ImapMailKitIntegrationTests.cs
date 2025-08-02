using System.Net;
using System.Net.Sockets;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Imap;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using Frimerki.Tests.Utilities;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Frimerki.Tests.Protocols.Imap;

/// <summary>
/// Integration tests for IMAP server using MailKit client
/// </summary>
public class ImapMailKitIntegrationTests : IAsyncDisposable {
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly ImapServer _imapServer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _serverTask;
    private readonly int _testPort; // Dynamic port assignment

    public ImapMailKitIntegrationTests(ITestOutputHelper output) {
        _output = output;
        _testPort = TestPortProvider.GetNextPort(); // Get unique port for this test instance
        _cancellationTokenSource = new CancellationTokenSource();

        // Setup mock services
        var mockUserService = new Mock<IUserService>();
        var mockFolderService = new Mock<IFolderService>();
        var mockMessageService = new Mock<IMessageService>();

        // Setup test user authentication
        mockUserService
            .Setup(x => x.AuthenticateUserEntityAsync("testuser", "testpass"))
            .ReturnsAsync(new User {
                Id = 1,
                Username = "testuser",
                DomainId = 1,
                PasswordHash = "hash",
                Salt = "salt",
                CanLogin = true
            });

        mockUserService
            .Setup(x => x.AuthenticateUserEntityAsync("testuser", "wrongpass"))
            .ReturnsAsync((User?)null);

        // Setup mock message service for APPEND tests
        mockMessageService
            .Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 123, Subject = "Test Message", Uid = 123 });

        mockMessageService
            .Setup(x => x.UpdateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 123, Subject = "Test Message", Uid = 123 });

        // Setup test services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(mockUserService.Object);
        services.AddSingleton(mockFolderService.Object);
        services.AddSingleton(mockMessageService.Object);

        _serviceProvider = services.BuildServiceProvider();

        // Start IMAP server on test port
        var logger = _serviceProvider.GetRequiredService<ILogger<ImapServer>>();
        _imapServer = new ImapServer(logger, _serviceProvider, _testPort);

        _serverTask = Task.Run(async () => {
            try {
                await _imapServer.StartAsync(_cancellationTokenSource.Token);
            } catch (OperationCanceledException) {
                // Expected when test completes
            }
        });

        // Wait a moment for server to start
        Thread.Sleep(200);
    }

    [Fact]
    public async Task MailKit_CanConnectToImapServer() {
        using var client = new ImapClient();

        // Test connection
        await client.ConnectAsync("localhost", _testPort, false);

        Assert.True(client.IsConnected);
        Assert.False(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanGetCapabilities() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Check capabilities
        var capabilities = client.Capabilities;
        Assert.True(capabilities.HasFlag(ImapCapabilities.IMAP4rev1));

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanAuthenticateWithValidCredentials() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Test authentication with valid credentials
        await client.AuthenticateAsync("testuser", "testpass");

        Assert.True(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CannotAuthenticateWithInvalidCredentials() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Test authentication with invalid credentials
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            await client.AuthenticateAsync("testuser", "wrongpass");
        });

        Assert.False(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanSelectInbox() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);
        await client.AuthenticateAsync("testuser", "testpass");

        // Select INBOX
        var inbox = await client.GetFolderAsync("INBOX");
        await inbox.OpenAsync(FolderAccess.ReadWrite);

        Assert.True(inbox.IsOpen);
        Assert.Equal(FolderAccess.ReadWrite, inbox.Access);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanExamineInbox() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);
        await client.AuthenticateAsync("testuser", "testpass");

        // Examine INBOX (read-only)
        var inbox = await client.GetFolderAsync("INBOX");
        await inbox.OpenAsync(FolderAccess.ReadOnly);

        Assert.True(inbox.IsOpen);
        Assert.Equal(FolderAccess.ReadOnly, inbox.Access);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanListFolders() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);
        await client.AuthenticateAsync("testuser", "testpass");

        // List folders
        var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0]);

        Assert.NotEmpty(folders);
        Assert.Contains(folders, f => f.Name == "INBOX");

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanHandleNoop() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);
        await client.AuthenticateAsync("testuser", "testpass");

        // Test NOOP command
        await client.NoOpAsync();

        // Should still be connected and authenticated
        Assert.True(client.IsConnected);
        Assert.True(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_HandlesLogoutGracefully() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);
        await client.AuthenticateAsync("testuser", "testpass");

        Assert.True(client.IsAuthenticated);

        // Disconnect should send LOGOUT
        await client.DisconnectAsync(true);

        Assert.False(client.IsConnected);
        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public async Task MailKit_CanHandleMultipleConnections() {
        var tasks = new List<Task>();

        for (int i = 0; i < 3; i++) {
            tasks.Add(Task.Run(async () => {
                using var client = new ImapClient();

                await client.ConnectAsync("localhost", _testPort, false);
                await client.AuthenticateAsync("testuser", "testpass");

                var inbox = await client.GetFolderAsync("INBOX");
                await inbox.OpenAsync(FolderAccess.ReadWrite);

                // Hold connection briefly
                await Task.Delay(100);

                await client.DisconnectAsync(true);
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task MailKit_RejectsCommandsBeforeAuthentication() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Should not be able to select folder before authentication
        await Assert.ThrowsAsync<ServiceNotAuthenticatedException>(async () => {
            var inbox = await client.GetFolderAsync("INBOX");
            await inbox.OpenAsync(FolderAccess.ReadWrite);
        });

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_ValidatesImapProtocolCompliance() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // MailKit will validate that our server sends proper IMAP responses
        var capabilities = client.Capabilities;

        // If we get here without exceptions, our server is sending valid IMAP protocol
        Assert.True(capabilities.HasFlag(ImapCapabilities.IMAP4rev1));

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task MailKit_CanAppendDraftMessage() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);
        await client.AuthenticateAsync("testuser", "testpass");

        // Create a simple draft message
        var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress("Test User", "testuser@localhost"));
        message.To.Add(new MimeKit.MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Test Draft Message";
        message.Body = new MimeKit.TextPart("plain") {
            Text = "This is a test draft message created via IMAP APPEND."
        };

        // Get Drafts folder (or INBOX if Drafts doesn't exist)
        try {
            var drafts = await client.GetFolderAsync("Drafts");
            await drafts.OpenAsync(FolderAccess.ReadWrite);

            // Append the message with Draft flag
            try {
                var uid = await drafts.AppendAsync(message, MessageFlags.Draft);

                // Verify the message was created
                Assert.True(uid.HasValue, "UID should have a value after APPEND");
                Assert.True(uid.Value.Id > 0, "UID should be greater than 0");

                await client.DisconnectAsync(true);
            } catch (Exception ex) {
                throw new Exception($"AppendAsync failed: {ex.Message}", ex);
            }
        } catch (FolderNotFoundException) {
            // If Drafts folder doesn't exist, append to INBOX with Draft flag
            var inbox = await client.GetFolderAsync("INBOX");
            await inbox.OpenAsync(FolderAccess.ReadWrite);

            try {
                var uid = await inbox.AppendAsync(message, MessageFlags.Draft);

                Assert.True(uid.HasValue, "UID should have a value after APPEND");
                Assert.True(uid.Value.Id > 0, "UID should be greater than 0");

                await client.DisconnectAsync(true);
            } catch (Exception ex) {
                throw new Exception($"AppendAsync to INBOX failed: {ex.Message}", ex);
            }
        }
    }

    public async ValueTask DisposeAsync() {
        _cancellationTokenSource.Cancel();

        try {
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        } catch (TimeoutException) {
            _output.WriteLine("Server task did not complete within timeout");
        }

        _imapServer?.Dispose();
        _cancellationTokenSource.Dispose();

        if (_serviceProvider is IAsyncDisposable asyncDisposable) {
            await asyncDisposable.DisposeAsync();
        } else if (_serviceProvider is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}
