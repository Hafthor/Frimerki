using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.DTOs.Folder;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Imap;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Frimerki.Tests.Protocols.Imap;

/// <summary>
/// Simple XUnit logger provider for test output
/// </summary>
public class XUnitLoggerProvider : ILoggerProvider {
    private readonly ITestOutputHelper _output;

    public XUnitLoggerProvider(ITestOutputHelper output) {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName) {
        return new XUnitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// Simple XUnit logger implementation
/// </summary>
public class XUnitLogger : ILogger {
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XUnitLogger(ITestOutputHelper output, string categoryName) {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        try {
            _output.WriteLine($"[{logLevel}] {_categoryName}: {formatter(state, exception)}");
        } catch {
            // Ignore logging failures in tests
        }
    }

    private class NullDisposable : IDisposable {
        public static NullDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Integration tests for IMAP server using MailKit client
/// </summary>
public class ImapServerIntegrationTests : IAsyncDisposable {
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly EmailDbContext _context;
    private readonly ImapServer _imapServer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _serverTask;
    private const int TestPort = 8993; // Use different port for testing

    public ImapServerIntegrationTests(ITestOutputHelper output) {
        _output = output;
        _cancellationTokenSource = new CancellationTokenSource();

        // Setup test services
        var services = new ServiceCollection();
        services.AddLogging(builder => {
            builder.AddProvider(new XUnitLoggerProvider(output));
        });

        // Add in-memory database for testing
        services.AddDbContext<EmailDbContext>(options =>
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));

        // Add required services (mocked for testing)
        services.AddScoped<IUserService, TestUserService>();
        services.AddScoped<IFolderService, TestFolderService>();
        services.AddScoped<IMessageService, TestMessageService>();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<EmailDbContext>();

        // Setup test data
        SetupTestData();

        // Start IMAP server on test port
        var logger = _serviceProvider.GetRequiredService<ILogger<ImapServer>>();
        _imapServer = new ImapServer(logger, _serviceProvider, TestPort);

        _serverTask = Task.Run(async () => {
            try {
                await _imapServer.StartAsync(_cancellationTokenSource.Token);
            } catch (OperationCanceledException) {
                // Expected when test completes
            }
        });

        // Wait a moment for server to start
        Thread.Sleep(100);
    }

    private void SetupTestData() {
        // Add test user
        var testUser = new User {
            Id = 1,
            Username = "testuser",
            DomainId = 1,
            PasswordHash = "$2a$11$test.hash.for.password", // This should match "testpass" when properly hashed
            Salt = "testsalt",
            CanLogin = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(testUser);
        _context.SaveChanges();
    }

    [Fact]
    public async Task ImapClient_CanConnectToServer() {
        using var client = new ImapClient();

        // Test connection
        await client.ConnectAsync("localhost", TestPort, false);

        Assert.True(client.IsConnected);
        Assert.False(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CanGetCapabilities() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", TestPort, false);

        // Check capabilities
        var capabilities = client.Capabilities;
        Assert.True(capabilities.HasFlag(ImapCapabilities.IMAP4rev1));

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CanAuthenticateWithValidCredentials() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", TestPort, false);

        // Test authentication with valid credentials
        await client.AuthenticateAsync("testuser", "testpass");

        Assert.True(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CannotAuthenticateWithInvalidCredentials() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", TestPort, false);

        // Test authentication with invalid credentials
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            await client.AuthenticateAsync("testuser", "wrongpass");
        });

        Assert.False(client.IsAuthenticated);

        await client.DisconnectAsync(true);
    }

    [Fact]
    public async Task ImapClient_CanSelectInbox() {
        using var client = new ImapClient();

        await client.ConnectAsync("localhost", TestPort, false);
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

        await client.ConnectAsync("localhost", TestPort, false);
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

        await client.ConnectAsync("localhost", TestPort, false);
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

        await client.ConnectAsync("localhost", TestPort, false);
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

        await client.ConnectAsync("localhost", TestPort, false);
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

                await client.ConnectAsync("localhost", TestPort, false);
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

        await client.ConnectAsync("localhost", TestPort, false);

        // Should not be able to select folder before authentication
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            var inbox = await client.GetFolderAsync("INBOX");
            await inbox.OpenAsync(FolderAccess.ReadWrite);
        });

        await client.DisconnectAsync(true);
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
        await _context.DisposeAsync();
        if (_serviceProvider is IAsyncDisposable asyncDisposable) {
            await asyncDisposable.DisposeAsync();
        } else if (_serviceProvider is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// Test implementation of IUserService for testing
/// </summary>
public class TestUserService : IUserService {
    public Task<User?> AuthenticateUserEntityAsync(string username, string password) {
        // Simple test authentication
        if (username == "testuser" && password == "testpass") {
            return Task.FromResult<User?>(new User {
                Id = 1,
                Username = "testuser",
                DomainId = 1,
                CanLogin = true
            });
        }
        return Task.FromResult<User?>(null);
    }

    public Task<PaginatedInfo<UserResponse>> GetUsersAsync(int page = 1, int pageSize = 50, string? domainFilter = null) =>
        throw new NotImplementedException();

    public Task<UserResponse?> GetUserByEmailAsync(string email) =>
        throw new NotImplementedException();

    public Task<UserResponse> CreateUserAsync(CreateUserRequest request) =>
        throw new NotImplementedException();

    public Task<UserResponse?> UpdateUserAsync(string email, UserUpdateRequest request) =>
        throw new NotImplementedException();

    public Task<bool> UpdateUserPasswordAsync(string email, UserPasswordUpdateRequest request) =>
        throw new NotImplementedException();

    public Task<bool> DeleteUserAsync(string email) =>
        throw new NotImplementedException();

    public Task<UserStatsResponse> GetUserStatsAsync(string email) =>
        throw new NotImplementedException();

    public Task<bool> UserExistsAsync(string email) =>
        throw new NotImplementedException();

    public Task<UserResponse?> AuthenticateUserAsync(string email, string password) =>
        throw new NotImplementedException();

    public Task<User?> GetUserEntityByEmailAsync(string email) =>
        throw new NotImplementedException();

    public Task<bool> ValidateEmailFormatAsync(string email) =>
        throw new NotImplementedException();
}

/// <summary>
/// Test implementation of IFolderService for testing
/// </summary>
public class TestFolderService : IFolderService {
    public Task<List<FolderListResponse>> GetFoldersAsync(int userId) =>
        throw new NotImplementedException();

    public Task<FolderResponse?> GetFolderAsync(int userId, string folderName) =>
        throw new NotImplementedException();

    public Task<FolderResponse> CreateFolderAsync(int userId, FolderRequest request) =>
        throw new NotImplementedException();

    public Task<FolderResponse?> UpdateFolderAsync(int userId, string folderName, FolderUpdateRequest request) =>
        throw new NotImplementedException();

    public Task<bool> DeleteFolderAsync(int userId, string folderName) =>
        throw new NotImplementedException();
}

/// <summary>
/// Test implementation of IMessageService for testing
/// </summary>
public class TestMessageService : IMessageService {
    public Task<PaginatedInfo<MessageListItemResponse>> GetMessagesAsync(int userId, MessageFilterRequest request) =>
        throw new NotImplementedException();

    public Task<MessageResponse?> GetMessageAsync(int userId, int messageId) =>
        throw new NotImplementedException();

    public Task<MessageResponse> CreateMessageAsync(int userId, MessageRequest request) =>
        throw new NotImplementedException();

    public Task<MessageResponse?> UpdateMessageAsync(int userId, int messageId, MessageUpdateRequest request) =>
        throw new NotImplementedException();

    public Task<bool> DeleteMessageAsync(int userId, int messageId) =>
        throw new NotImplementedException();
}
