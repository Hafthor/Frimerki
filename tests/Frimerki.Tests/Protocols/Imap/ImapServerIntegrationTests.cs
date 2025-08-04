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
using Xunit.Abstractions;

namespace Frimerki.Tests.Protocols.Imap;

/// <summary>
/// Simple XUnit logger provider for test output
/// </summary>
public sealed class XUnitLoggerProvider(ITestOutputHelper output) : ILoggerProvider {
    public ILogger CreateLogger(string categoryName) => new XUnitLogger(output, categoryName);

    public void Dispose() { }
}

/// <summary>
/// Simple XUnit logger implementation
/// </summary>
public class XUnitLogger(ITestOutputHelper output, string categoryName) : ILogger {
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
        try {
            output.WriteLine($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
        } catch {
            // Ignore logging failures in tests
        }
    }

    private sealed class NullDisposable : IDisposable {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Integration tests for IMAP server using MailKit client
/// </summary>
[Collection("MailKit")]
public class ImapServerIntegrationTests : IAsyncDisposable {
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly ImapServer _imapServer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _serverTask;
    private readonly int _testPort; // Dynamic port assignment

    public ImapServerIntegrationTests(ITestOutputHelper output) {
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
            .ReturnsAsync((User)null);

        // Setup test services
        var services = new ServiceCollection();
        services.AddLogging(builder => {
            builder.AddProvider(new XUnitLoggerProvider(output));
        });
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
        Thread.Sleep(500);
    }

    [Fact]
    public async Task ImapClient_CanConnectToServer() {
        using var client = new ImapClient();

        // Test connection
        await client.ConnectAsync("localhost", _testPort, false);

        Assert.True(client.IsConnected);
        Assert.False(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CanGetCapabilities() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Check capabilities
        var capabilities = client.Capabilities;
        Assert.True(capabilities.HasFlag(ImapCapabilities.IMAP4rev1));

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CanAuthenticateWithValidCredentials() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Test authentication with valid credentials
        await client.AuthenticateAsync("testuser", "testpass");

        Assert.True(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CannotAuthenticateWithInvalidCredentials() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Test authentication with invalid credentials
        await Assert.ThrowsAnyAsync<AuthenticationException>(async () => {
            await client.AuthenticateAsync("testuser", "wrongpass");
        });

        Assert.False(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CanSelectInbox() {
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
    public async Task ImapClient_CanExamineInbox() {
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
    public async Task ImapClient_CanListFolders() {
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
    public async Task ImapClient_CanHandleNoop() {
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
    public async Task ImapClient_HandlesLogoutGracefully() {
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
    public async Task ImapClient_CanHandleMultipleConnections() {
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
    public async Task ImapClient_RejectsCommandsBeforeAuthentication() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", _testPort, false);

        // Should not be able to select folder before authentication
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            var inbox = await client.GetFolderAsync("INBOX");
            await inbox.OpenAsync(FolderAccess.ReadWrite);
        });

        await client.DisconnectAsync(true);
    }

    public async ValueTask DisposeAsync() {
        await _cancellationTokenSource.CancelAsync();

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
