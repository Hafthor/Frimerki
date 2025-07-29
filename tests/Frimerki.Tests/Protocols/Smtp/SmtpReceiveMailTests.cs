using System.Net.Sockets;
using System.Text;
using Frimerki.Data;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Smtp;
using Frimerki.Services;
using Frimerki.Services.Email;
using Frimerki.Services.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Frimerki.Tests.Protocols.Smtp;

/// <summary>
/// Integration tests for SMTP email receiving functionality
/// </summary>
public class SmtpReceiveMailTests : IAsyncDisposable {
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly EmailDbContext _context;
    private readonly SmtpServer _smtpServer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _serverTask;
    private const int BaseTestPort = 2525;
    private static int _testCounter = 0;
    private readonly int _testPort;

    public SmtpReceiveMailTests(ITestOutputHelper output) {
        _output = output;
        _cancellationTokenSource = new CancellationTokenSource();

        // Create unique database name and port per test instance
        var testId = Interlocked.Increment(ref _testCounter);
        _testPort = BaseTestPort + testId;
        var dbName = $"SmtpTestDb_{testId}_{Guid.NewGuid():N}";

        // Setup services
        var services = new ServiceCollection();
        services.AddLogging(logging => {
            logging.AddDebug();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddDbContext<EmailDbContext>(options => {
            options.UseInMemoryDatabase(dbName);
            options.EnableSensitiveDataLogging();
        });

        services.AddFrimerkiServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<EmailDbContext>();

        // Initialize database
        _context.Database.EnsureCreated();
        InitializeTestData().Wait();

        var logger = _serviceProvider.GetRequiredService<ILogger<SmtpServer>>();
        _smtpServer = new SmtpServer(logger, _serviceProvider, _testPort);
        _serverTask = _smtpServer.StartAsync(_cancellationTokenSource.Token);

        // Wait for server to start
        Task.Delay(500, _cancellationTokenSource.Token).Wait();
    }

    private async Task InitializeTestData() {
        // Add test domain
        var domain = new Domain {
            Id = 1,
            Name = "example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Add test user
        var user = new User {
            Id = 1,
            Username = "testuser",
            DomainId = 1,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("testpass"),
            Salt = "testsalt",
            FullName = "Test User",
            CanReceive = true,
            CanLogin = true,
            Role = "User",
            CreatedAt = DateTime.UtcNow,
            Domain = domain
        };

        // Add INBOX folder for the user
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

        _context.Domains.Add(domain);
        _context.Users.Add(user);
        _context.Folders.Add(inboxFolder);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task SmtpServer_CanReceiveAndDeliverEmail() {
        // Arrange
        const string testMessage = """
            From: sender@external.com
            To: testuser@example.com
            Subject: Test Email
            Date: Mon, 29 Jul 2025 12:00:00 +0000
            Message-ID: <test123@external.com>

            This is a test email body.
            """;

        // Act - Connect to SMTP server and send email
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", _testPort);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        // Read greeting
        var greeting = await reader.ReadLineAsync();
        Assert.NotNull(greeting);
        Assert.StartsWith("220", greeting);

        // Send EHLO
        await writer.WriteLineAsync("EHLO testclient.com");
        var ehloResponse = await reader.ReadLineAsync();
        Assert.NotNull(ehloResponse);
        Assert.StartsWith("250", ehloResponse);

        // Skip additional EHLO responses
        string? line;
        while ((line = await reader.ReadLineAsync()) != null && line.StartsWith("250-")) {
            // Read remaining EHLO responses
        }

        // Send MAIL FROM
        await writer.WriteLineAsync("MAIL FROM:<sender@external.com>");
        var mailResponse = await reader.ReadLineAsync();
        Assert.NotNull(mailResponse);
        Assert.StartsWith("250", mailResponse);

        // Send RCPT TO
        await writer.WriteLineAsync("RCPT TO:<testuser@example.com>");
        var rcptResponse = await reader.ReadLineAsync();
        Assert.NotNull(rcptResponse);
        Assert.StartsWith("250", rcptResponse);

        // Send DATA command
        await writer.WriteLineAsync("DATA");
        var dataResponse = await reader.ReadLineAsync();
        Assert.NotNull(dataResponse);
        Assert.StartsWith("354", dataResponse);

        // Send message data
        foreach (var messageLine in testMessage.Split('\n')) {
            await writer.WriteLineAsync(messageLine.TrimEnd('\r'));
        }
        await writer.WriteLineAsync(".");

        var messageResponse = await reader.ReadLineAsync();
        Assert.NotNull(messageResponse);
        Assert.StartsWith("250", messageResponse);

        // Send QUIT
        await writer.WriteLineAsync("QUIT");
        var quitResponse = await reader.ReadLineAsync();
        Assert.NotNull(quitResponse);
        Assert.StartsWith("221", quitResponse);

        // Wait a moment for message processing
        await Task.Delay(100);

        // Refresh the context to see changes made by the EmailDeliveryService
        _context.ChangeTracker.Clear();

        // Assert - Verify message was delivered to user's INBOX
        var deliveredMessage = await _context.UserMessages
            .Include(um => um.Message)
            .Include(um => um.Folder)
            .Where(um => um.UserId == 1 && um.Folder.SystemFolderType == "INBOX")
            .FirstOrDefaultAsync();

        Assert.NotNull(deliveredMessage);
        Assert.Equal("Test Email", deliveredMessage.Message.Subject);
        Assert.Equal("sender@external.com", deliveredMessage.Message.FromAddress);
        Assert.Equal("testuser@example.com", deliveredMessage.Message.ToAddress);
        Assert.Contains("This is a test email body", deliveredMessage.Message.Body);

        // Verify folder statistics were updated
        var inbox = await _context.Folders
            .Where(f => f.Id == 1)
            .FirstOrDefaultAsync();

        Assert.NotNull(inbox);
        Assert.Equal(1, inbox.Exists);
        Assert.Equal(1, inbox.Recent);
        Assert.Equal(1, inbox.Unseen);
    }

    [Fact]
    public async Task SmtpServer_RejectsEmailForNonExistentUser() {
        // Act - Try to send email to non-existent user
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", _testPort);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        // Read greeting
        await reader.ReadLineAsync();

        // Send EHLO
        await writer.WriteLineAsync("EHLO testclient.com");
        await reader.ReadLineAsync();

        // Skip additional EHLO responses
        string? line;
        while ((line = await reader.ReadLineAsync()) != null && line.StartsWith("250-")) {
            // Read remaining EHLO responses
        }

        // Send MAIL FROM
        await writer.WriteLineAsync("MAIL FROM:<sender@external.com>");
        await reader.ReadLineAsync();

        // Send RCPT TO with non-existent user
        await writer.WriteLineAsync("RCPT TO:<nonexistent@example.com>");
        var rcptResponse = await reader.ReadLineAsync();
        Assert.NotNull(rcptResponse);
        Assert.StartsWith("250", rcptResponse); // SMTP accepts during RCPT, delivery fails later

        // Send DATA command
        await writer.WriteLineAsync("DATA");
        await reader.ReadLineAsync();

        // Send simple message
        await writer.WriteLineAsync("Subject: Test");
        await writer.WriteLineAsync("");
        await writer.WriteLineAsync("Test body");
        await writer.WriteLineAsync(".");

        var messageResponse = await reader.ReadLineAsync();
        Assert.NotNull(messageResponse);
        // Should indicate delivery failure
        Assert.StartsWith("550", messageResponse);

        // Send QUIT
        await writer.WriteLineAsync("QUIT");
        await reader.ReadLineAsync();

        // Wait a moment for processing
        await Task.Delay(100);

        // Assert - Verify no message was delivered
        var messageCount = await _context.UserMessages.CountAsync();
        Assert.Equal(0, messageCount);
    }

    public async ValueTask DisposeAsync() {
        _cancellationTokenSource.Cancel();

        try {
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        } catch (TimeoutException) {
            _output.WriteLine("Server task did not complete within timeout");
        }

        _smtpServer?.Dispose();
        _cancellationTokenSource.Dispose();
        await _context.DisposeAsync();
        if (_serviceProvider is IAsyncDisposable asyncDisposable) {
            await asyncDisposable.DisposeAsync();
        } else if (_serviceProvider is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}
