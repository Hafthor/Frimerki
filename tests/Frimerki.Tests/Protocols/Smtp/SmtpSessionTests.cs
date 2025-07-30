using System.Net.Sockets;
using System.Text;
using Frimerki.Data;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Smtp;
using Frimerki.Services.Common;
using Frimerki.Services.Email;
using Frimerki.Services.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Frimerki.Tests.Protocols.Smtp;

public class SmtpSessionTests : IDisposable {
    private readonly Mock<IUserService> _mockUserService;
    private readonly EmailDeliveryService _emailDeliveryService;
    private readonly Mock<ILogger> _mockLogger;
    private readonly TcpListener _listener;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly EmailDbContext _context;
    private SmtpSession? _session;

    public SmtpSessionTests() {
        _mockUserService = new Mock<IUserService>();
        _mockLogger = new Mock<ILogger>();

        // Create in-memory database context for EmailDeliveryService
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new EmailDbContext(options);

        // Create real EmailDeliveryService with mocked dependencies
        var mockNowProvider = new Mock<INowProvider>();
        mockNowProvider.Setup(x => x.UtcNow).Returns(DateTime.UtcNow);
        var mockEmailLogger = new Mock<ILogger<EmailDeliveryService>>();

        _emailDeliveryService = new EmailDeliveryService(
            _context,
            _mockUserService.Object,
            mockNowProvider.Object,
            mockEmailLogger.Object);

        // Set up TCP connection for testing
        _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = _listener.LocalEndpoint as System.Net.IPEndPoint;

        _client = new TcpClient();
        _client.Connect(System.Net.IPAddress.Loopback, endpoint!.Port);
        _stream = _client.GetStream();
    }

    public void Dispose() {
        _session?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _listener?.Stop();
        _context?.Dispose();
    }

    [Fact]
    public async Task HandleAsync_SendsGreeting() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting
        var greeting = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("220 frímerki.local ESMTP Frímerki Mail Server", greeting);

        // Send QUIT to end session gracefully
        await writer.WriteLineAsync("QUIT");

        await handleTask;
    }

    [Fact]
    public async Task HandleHeloCommand_ReturnsSuccessResponse() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Use StreamReader/StreamWriter for proper line-oriented communication
        using var reader = new StreamReader(_stream, Encoding.UTF8);
        using var writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting line
        var greeting = await reader.ReadLineAsync(cts.Token);
        Assert.Contains("220", greeting);

        // Send HELO command
        await writer.WriteLineAsync("HELO test.example.com");

        // Read response
        var response = await reader.ReadLineAsync(cts.Token);

        // Assert
        Assert.Contains("250 frímerki.local Hello, pleased to meet you", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleEhloCommand_ReturnsExtendedResponse() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send EHLO command
        await writer.WriteLineAsync("EHLO test.example.com");

        // Read full EHLO response (multiple lines)
        var responses = new List<string?>();
        string? response;
        do {
            response = await reader.ReadLineAsync();
            if (response != null) {
                responses.Add(response);
            }
        } while (response != null && response.Length > 3 && response[3] == '-');

        var fullResponse = string.Join("\n", responses);

        // Assert
        Assert.Contains("250-frímerki.local Hello, pleased to meet you", fullResponse);
        Assert.Contains("250-AUTH PLAIN LOGIN", fullResponse);
        Assert.Contains("250-8BITMIME", fullResponse);
        Assert.Contains("250 ENHANCEDSTATUSCODES", fullResponse);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleAuthPlain_WithValidCredentials_ReturnsSuccess() {
        // Arrange
        var testUser = new User { Id = 1, Username = "test@example.com" };
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("test@example.com", "password"))
                       .ReturnsAsync(testUser);

        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send EHLO command
        await writer.WriteLineAsync("EHLO test.example.com");

        // Read EHLO response (multiple lines - skip until we get the final line)
        string? response;
        do {
            response = await reader.ReadLineAsync();
        } while (response != null && response.Length > 3 && response[3] == '-');

        // Send AUTH PLAIN with credentials (format: \0username\0password)
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0test@example.com\0password"));
        await writer.WriteLineAsync($"AUTH PLAIN {credentials}");

        // Read response
        response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("235 Authentication successful", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleAuthPlain_WithInvalidCredentials_ReturnsFailure() {
        // Arrange
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync(It.IsAny<string>(), It.IsAny<string>()))
                       .ReturnsAsync((User?)null);

        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send EHLO command
        await writer.WriteLineAsync("EHLO test.example.com");

        // Read EHLO response (multiple lines - skip until we get the final line)
        string? response;
        do {
            response = await reader.ReadLineAsync();
        } while (response != null && response.Length > 3 && response[3] == '-');

        // Send AUTH PLAIN with invalid credentials
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0invalid@example.com\0wrongpassword"));
        await writer.WriteLineAsync($"AUTH PLAIN {credentials}");

        // Read response
        response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("535 Authentication failed", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleAuthLogin_WithValidCredentials_ReturnsSuccess() {
        // Arrange
        var testUser = new User { Id = 1, Username = "test@example.com" };
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("test@example.com", "password"))
                       .ReturnsAsync(testUser);

        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send EHLO command
        await writer.WriteLineAsync("EHLO test.example.com");

        // Read EHLO response (multiple lines - skip until we get the final line)
        string? response;
        do {
            response = await reader.ReadLineAsync();
        } while (response != null && response.Length > 3 && response[3] == '-');

        // Send AUTH LOGIN
        await writer.WriteLineAsync("AUTH LOGIN");

        // Read "Username:" prompt
        var prompt = await reader.ReadLineAsync();
        Assert.Contains("334 VXNlcm5hbWU6", prompt); // "Username:" in base64

        // Send username (base64 encoded)
        var username = Convert.ToBase64String(Encoding.UTF8.GetBytes("test@example.com"));
        await writer.WriteLineAsync(username);

        // Read "Password:" prompt
        prompt = await reader.ReadLineAsync();
        Assert.Contains("334 UGFzc3dvcmQ6", prompt); // "Password:" in base64

        // Send password (base64 encoded)
        var password = Convert.ToBase64String(Encoding.UTF8.GetBytes("password"));
        await writer.WriteLineAsync(password);

        // Read response
        response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("235 Authentication successful", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleMailFrom_AfterEhlo_ReturnsSuccess() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send EHLO command
        await writer.WriteLineAsync("EHLO test.example.com");

        // Read EHLO response (multiple lines - skip until we get the final line)
        string? response;
        do {
            response = await reader.ReadLineAsync();
        } while (response != null && response.Length > 3 && response[3] == '-');

        // Send MAIL FROM command
        await writer.WriteLineAsync("MAIL FROM:<sender@example.com>");

        // Read response
        response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleRcptTo_AfterMailFrom_ReturnsSuccess() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send EHLO command
        await writer.WriteLineAsync("EHLO test.example.com");

        // Read EHLO response (multiple lines - skip until we get the final line)
        string? response;
        do {
            response = await reader.ReadLineAsync();
        } while (response != null && response.Length > 3 && response[3] == '-');

        // Send MAIL FROM command
        await writer.WriteLineAsync("MAIL FROM:<sender@example.com>");
        await reader.ReadLineAsync(); // Read MAIL FROM response

        // Send RCPT TO command
        await writer.WriteLineAsync("RCPT TO:<recipient@example.com>");

        // Read response
        response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleUnknownCommand_ReturnsError() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send unknown command
        await writer.WriteLineAsync("UNKNOWN");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("500 Syntax error, command unrecognized", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleNoop_ReturnsOk() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Use StreamReader/StreamWriter for proper line-oriented communication
        using var reader = new StreamReader(_stream, Encoding.UTF8);
        using var writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting line
        var greeting = await reader.ReadLineAsync(cts.Token);
        Assert.Contains("220", greeting);

        // Send NOOP command
        await writer.WriteLineAsync("NOOP");

        // Read response
        var response = await reader.ReadLineAsync(cts.Token);

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleRset_ReturnsOk() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Use StreamReader/StreamWriter for proper line-oriented communication
        using var reader = new StreamReader(_stream, Encoding.UTF8);
        using var writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting line
        var greeting = await reader.ReadLineAsync(cts.Token);
        Assert.Contains("220", greeting);

        // Send RSET command
        await writer.WriteLineAsync("RSET");

        // Read response
        var response = await reader.ReadLineAsync(cts.Token);

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await writer.WriteLineAsync("QUIT");
        await handleTask;
    }

    [Fact]
    public async Task HandleHelp_ReturnsOk() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Use StreamReader/StreamWriter for proper line-oriented communication
        using var reader = new StreamReader(_stream, Encoding.UTF8);
        using var writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting line
        var greeting = await reader.ReadLineAsync(cts.Token);
        Assert.Contains("220", greeting);

        // Send HELP command
        await writer.WriteLineAsync("HELP");

        // Read multi-line HELP response
        var line1 = await reader.ReadLineAsync(cts.Token);
        var line2 = await reader.ReadLineAsync(cts.Token);
        var line3 = await reader.ReadLineAsync(cts.Token);
        var line4 = await reader.ReadLineAsync(cts.Token);

        // Assert that we got the expected HELP response
        Assert.Contains("214-This is Frímerki Mail Server", line1);
        Assert.Contains("214-Commands supported:", line2);
        Assert.Contains("214-  HELO EHLO AUTH MAIL RCPT DATA RSET NOOP QUIT HELP", line3);
        Assert.Contains("214 End of HELP info", line4);

        // Send QUIT to end session gracefully
        await writer.WriteLineAsync("QUIT");

        await handleTask;
    }
}
