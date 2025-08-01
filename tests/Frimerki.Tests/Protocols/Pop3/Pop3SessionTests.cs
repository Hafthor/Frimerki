using System.Net.Sockets;
using System.Text;
using Frimerki.Models.DTOs;
using Frimerki.Protocols.Pop3;
using Frimerki.Services.Message;
using Frimerki.Services.Session;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Frimerki.Tests.Protocols.Pop3;

public class Pop3SessionTests : IDisposable {
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<IMessageService> _mockMessageService;
    private readonly Mock<ILogger<Pop3Session>> _mockLogger;
    private readonly TcpListener _listener;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public Pop3SessionTests() {
        _mockSessionService = new Mock<ISessionService>();
        _mockMessageService = new Mock<IMessageService>();
        _mockLogger = new Mock<ILogger<Pop3Session>>();

        // Set up TCP connection for testing
        _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = _listener.LocalEndpoint as System.Net.IPEndPoint;

        _client = new TcpClient();
        _client.Connect(System.Net.IPAddress.Loopback, endpoint!.Port);
        _stream = _client.GetStream();
    }

    public void Dispose() {
        _stream?.Dispose();
        _client?.Dispose();
        _listener?.Stop();
    }

    private async Task<(StreamReader reader, StreamWriter writer, Task handleTask)> SetupSessionAsync() {
        var serverClient = await _listener.AcceptTcpClientAsync();
        var session = new Pop3Session(_mockSessionService.Object, _mockMessageService.Object, _mockLogger.Object);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(_stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true };

        var handleTask = session.HandleAsync(serverClient, cts.Token);

        return (reader, writer, handleTask);
    }

    [Fact]
    public async Task HandleAsync_SendsGreeting() {
        // Arrange & Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting
        var greeting = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("+OK Frimerki POP3 Server Ready", greeting);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandleCapaCommand_ReturnsCapabilities() {
        // Arrange & Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send CAPA command
        await writer.WriteLineAsync("CAPA");

        // Read multi-line CAPA response
        var responses = new List<string>();
        string? response;
        do {
            response = await reader.ReadLineAsync();
            if (response != null) {
                responses.Add(response);
            }
        } while (response != null && response != ".");

        var fullResponse = string.Join("\n", responses);

        // Assert
        Assert.Contains("+OK Capability list follows", fullResponse);
        Assert.Contains("USER", fullResponse);
        Assert.Contains("TOP", fullResponse);
        Assert.Contains("UIDL", fullResponse);
        Assert.Contains(".", fullResponse); // End marker

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandleUserCommand_SetsUsername() {
        // Arrange & Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send USER command
        await writer.WriteLineAsync("USER test@example.com");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("+OK User accepted", response);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandlePassCommand_WithValidCredentials_ReturnsSuccess() {
        // Arrange
        var testUser = new UserSessionInfo { Id = 1, Username = "test@example.com", Email = "test@example.com" };
        var loginResponse = new LoginResponse {
            User = testUser,
            Token = "test-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _mockSessionService.Setup(x => x.LoginAsync(It.Is<LoginRequest>(r => r.Email == "test@example.com" && r.Password == "password")))
                          .ReturnsAsync(loginResponse);

        // Mock messages for the user
        var messageItems = new List<MessageListItemResponse> {
            new() { Id = 1, MessageSize = 100 }
        };
        var paginatedResult = new PaginatedInfo<MessageListItemResponse> {
            Items = messageItems,
            TotalCount = 1
        };
        _mockMessageService.Setup(x => x.GetMessagesAsync(It.IsAny<int>(), It.IsAny<MessageFilterRequest>()))
                          .ReturnsAsync(paginatedResult);

        // Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send USER command
        await writer.WriteLineAsync("USER test@example.com");
        await reader.ReadLineAsync(); // Read USER response

        // Send PASS command
        await writer.WriteLineAsync("PASS password");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("+OK", response);
        Assert.Contains("messages", response);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandlePassCommand_WithInvalidCredentials_ReturnsFailure() {
        // Arrange
        _mockSessionService.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>()))
                          .ReturnsAsync((LoginResponse?)null);

        // Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send USER command
        await writer.WriteLineAsync("USER invalid@example.com");
        await reader.ReadLineAsync(); // Read USER response

        // Send PASS command
        await writer.WriteLineAsync("PASS wrongpassword");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("-ERR Authentication failed", response);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandleNoopCommand_ReturnsOk() {
        // Arrange & Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send NOOP command
        await writer.WriteLineAsync("NOOP");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("+OK", response);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandleUnknownCommand_ReturnsError() {
        // Arrange & Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send unknown command
        await writer.WriteLineAsync("UNKNOWN");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("-ERR Unknown command", response);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }

    [Fact]
    public async Task HandleInvalidCommand_ReturnsError() {
        // Arrange & Act
        var (reader, writer, handleTask) = await SetupSessionAsync();

        // Read greeting and discard
        await reader.ReadLineAsync();

        // Send invalid command (just spaces)
        await writer.WriteLineAsync("   ");

        // Read response
        var response = await reader.ReadLineAsync();

        // Assert
        Assert.Contains("-ERR Invalid command", response);

        // Cleanup
        await writer.WriteLineAsync("QUIT");
        await writer.FlushAsync();
        await handleTask;
        reader.Dispose();
        writer.Dispose();
    }
}
