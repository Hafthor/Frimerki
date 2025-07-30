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

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting
        var buffer = new byte[1024];
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var greeting = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("220 frímerki.local ESMTP Frímerki Mail Server", greeting);

        // Send QUIT to end session gracefully
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);

        await handleTask;
    }

    [Fact]
    public async Task HandleHeloCommand_ReturnsSuccessResponse() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send HELO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("HELO test.example.com\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("250 frímerki.local Hello, pleased to meet you", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleEhloCommand_ReturnsExtendedResponse() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send EHLO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("EHLO test.example.com\r\n"), cts.Token);

        // Read full EHLO response (multiple lines)
        await Task.Delay(50, cts.Token); // Give time for all lines to be sent
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("250-frímerki.local Hello, pleased to meet you", response);
        Assert.Contains("250-AUTH PLAIN LOGIN", response);
        Assert.Contains("250-8BITMIME", response);
        Assert.Contains("250 ENHANCEDSTATUSCODES", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
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

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send EHLO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("EHLO test.example.com\r\n"), cts.Token);

        // Read EHLO response (multiple lines)
        await Task.Delay(50, cts.Token);
        await _stream.ReadAsync(buffer, cts.Token);

        // Send AUTH PLAIN with credentials (format: \0username\0password)
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0test@example.com\0password"));
        await _stream.WriteAsync(Encoding.UTF8.GetBytes($"AUTH PLAIN {credentials}\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("235 Authentication successful", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
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

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send EHLO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("EHLO test.example.com\r\n"), cts.Token);

        // Read EHLO response (multiple lines)
        await Task.Delay(50, cts.Token);
        await _stream.ReadAsync(buffer, cts.Token);

        // Send AUTH PLAIN with invalid credentials
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0invalid@example.com\0wrongpassword"));
        await _stream.WriteAsync(Encoding.UTF8.GetBytes($"AUTH PLAIN {credentials}\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("535 Authentication failed", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
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

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send EHLO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("EHLO test.example.com\r\n"), cts.Token);

        // Read EHLO response (multiple lines)
        await Task.Delay(50, cts.Token);
        await _stream.ReadAsync(buffer, cts.Token);

        // Send AUTH LOGIN
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("AUTH LOGIN\r\n"), cts.Token);

        // Read "Username:" prompt
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var prompt = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Assert.Contains("334 VXNlcm5hbWU6", prompt); // "Username:" in base64

        // Send username (base64 encoded)
        var username = Convert.ToBase64String(Encoding.UTF8.GetBytes("test@example.com"));
        await _stream.WriteAsync(Encoding.UTF8.GetBytes($"{username}\r\n"), cts.Token);

        // Read "Password:" prompt
        bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        prompt = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Assert.Contains("334 UGFzc3dvcmQ6", prompt); // "Password:" in base64

        // Send password (base64 encoded)
        var password = Convert.ToBase64String(Encoding.UTF8.GetBytes("password"));
        await _stream.WriteAsync(Encoding.UTF8.GetBytes($"{password}\r\n"), cts.Token);

        // Read response
        bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("235 Authentication successful", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleMailFrom_AfterEhlo_ReturnsSuccess() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send EHLO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("EHLO test.example.com\r\n"), cts.Token);

        // Read EHLO response (multiple lines)
        await Task.Delay(50, cts.Token);
        await _stream.ReadAsync(buffer, cts.Token);

        // Send MAIL FROM command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("MAIL FROM:<sender@example.com>\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleRcptTo_AfterMailFrom_ReturnsSuccess() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send EHLO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("EHLO test.example.com\r\n"), cts.Token);

        // Read EHLO response (multiple lines)
        await Task.Delay(50, cts.Token);
        await _stream.ReadAsync(buffer, cts.Token);

        // Send MAIL FROM command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("MAIL FROM:<sender@example.com>\r\n"), cts.Token);
        await _stream.ReadAsync(buffer, cts.Token); // Read MAIL FROM response

        // Send RCPT TO command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("RCPT TO:<recipient@example.com>\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleUnknownCommand_ReturnsError() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send unknown command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("UNKNOWN\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("500 Syntax error, command unrecognized", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleNoop_ReturnsOk() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send NOOP command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("NOOP\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleRset_ReturnsOk() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send RSET command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("RSET\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("250 OK", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }

    [Fact]
    public async Task HandleHelp_ReturnsOk() {
        // Arrange
        using var serverClient = await _listener.AcceptTcpClientAsync();
        _session = new SmtpSession(serverClient, _mockUserService.Object, _emailDeliveryService, _mockLogger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var handleTask = _session.HandleAsync(cts.Token);

        // Read greeting and discard
        var buffer = new byte[1024];
        await _stream.ReadAsync(buffer, cts.Token);

        // Send HELP command
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("HELP\r\n"), cts.Token);

        // Read response
        var bytesRead = await _stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Assert
        Assert.Contains("214", response);

        // End session
        await _stream.WriteAsync(Encoding.UTF8.GetBytes("QUIT\r\n"), cts.Token);
        await handleTask;
    }
}
