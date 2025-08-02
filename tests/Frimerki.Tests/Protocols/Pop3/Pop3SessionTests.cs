using System.Net.Sockets;
using System.Text;
using Frimerki.Models.DTOs;
using Frimerki.Protocols.Pop3;
using Frimerki.Services.Message;
using Frimerki.Services.Session;
using Microsoft.Extensions.Logging;
using Moq;

namespace Frimerki.Tests.Protocols.Pop3;

file static class ExtensionMethods {
    public static async Task WriteLineAndFlushAsync(this StreamWriter writer, string message) {
        await writer.WriteLineAsync(message);
        await writer.FlushAsync();
    }
}

[Collection("Pop3Tests")]
public class Pop3SessionTests {
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<IMessageService> _mockMessageService;
    private readonly Mock<ILogger<Pop3Session>> _mockLogger;

    public Pop3SessionTests() {
        _mockSessionService = new Mock<ISessionService>();
        _mockMessageService = new Mock<IMessageService>();
        _mockLogger = new Mock<ILogger<Pop3Session>>();
    }

    private async Task<(StreamReader reader, StreamWriter writer, Task handleTask, IDisposable cleanup)> SetupSessionAsync() {
        // Create fresh TCP connections for each test
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = listener.LocalEndpoint as System.Net.IPEndPoint;

        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, endpoint!.Port);
        var stream = client.GetStream();

        var serverClient = await listener.AcceptTcpClientAsync();
        var session = new Pop3Session(_mockSessionService.Object, _mockMessageService.Object, _mockLogger.Object);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true };

        var handleTask = session.HandleAsync(serverClient, cts.Token);

        // Return cleanup delegate
        var cleanup = new DisposableAction(() => {
            reader?.Dispose();
            writer?.Dispose();
            stream?.Dispose();
            client?.Dispose();
            serverClient?.Dispose();
            listener?.Stop();
        });

        return (reader, writer, handleTask, cleanup);
    }

    private class DisposableAction : IDisposable {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }

    [Fact]
    public async Task HandleAsync_SendsGreeting() {
        // Arrange & Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {
            // Read greeting
            var greeting = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("+OK Frimerki POP3 Server Ready", greeting);

            // Cleanup
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
    }

    [Fact]
    public async Task HandleCapaCommand_ReturnsCapabilities() {
        // Arrange & Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {
            // Read greeting and discard
            await reader.ReadLineAsync();

            // Send CAPA command
            await writer.WriteLineAndFlushAsync("CAPA");

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
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
    }

    [Fact]
    public async Task HandleUserCommand_SetsUsername() {
        // Arrange & Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {
            // Read greeting and discard
            await reader.ReadLineAsync();

            // Send USER command
            await writer.WriteLineAndFlushAsync("USER test@example.com");

            // Read response
            var response = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("+OK User accepted", response);

            // Cleanup
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
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
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {

            // Read greeting and discard
            await reader.ReadLineAsync();

            // Send USER command
            await writer.WriteLineAndFlushAsync("USER test@example.com");
            await reader.ReadLineAsync(); // Read USER response

            // Send PASS command
            await writer.WriteLineAndFlushAsync("PASS password");

            // Read response
            var response = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("+OK", response);
            Assert.Contains("messages", response);

            // Cleanup
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
    }

    [Fact]
    public async Task HandlePassCommand_WithInvalidCredentials_ReturnsFailure() {
        // Arrange
        _mockSessionService.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>()))
                          .ReturnsAsync((LoginResponse?)null);

        // Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {

            // Read greeting and discard
            await reader.ReadLineAsync();

            // Send USER command
            await writer.WriteLineAndFlushAsync("USER invalid@example.com");
            await reader.ReadLineAsync(); // Read USER response

            // Send PASS command
            await writer.WriteLineAndFlushAsync("PASS wrongpassword");

            // Read response
            var response = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("-ERR Authentication failed", response);

            // Cleanup
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
    }

    [Fact]
    public async Task HandleNoopCommand_ReturnsOk() {
        // Arrange - Reset mocks to ensure clean state
        _mockSessionService.Reset();
        _mockMessageService.Reset();
        _mockLogger.Reset();

        var testUser = new UserSessionInfo { Id = 1, Username = "test@example.com", Email = "test@example.com" };
        var loginResponse = new LoginResponse {
            User = testUser,
            Token = "test-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _mockSessionService.Setup(x => x.LoginAsync(It.Is<LoginRequest>(r => r.Email == "test@example.com" && r.Password == "password")))
                          .ReturnsAsync(loginResponse);

        // Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {

            // Read greeting and discard
            await reader.ReadLineAsync();

            // Authenticate first
            await writer.WriteLineAndFlushAsync("USER test@example.com");
            await reader.ReadLineAsync(); // Read USER response

            await writer.WriteLineAndFlushAsync("PASS password");
            await reader.ReadLineAsync(); // Read PASS response

            // Send NOOP command
            await writer.WriteLineAndFlushAsync("NOOP");

            // Read response
            var response = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("+OK", response);

            // Cleanup with error handling
            try {
                await writer.WriteLineAndFlushAsync("QUIT");
                await handleTask;
            } catch (IOException) {
                // Connection may have been closed, which is fine
            }
        }
    }

    [Fact]
    public async Task HandleUnknownCommand_ReturnsError() {
        // Arrange & Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {

            // Read greeting and discard
            await reader.ReadLineAsync();

            // Send unknown command
            await writer.WriteLineAndFlushAsync("UNKNOWN");

            // Read response
            var response = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("-ERR Unknown command", response);

            // Cleanup
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
    }

    [Fact]
    public async Task HandleInvalidCommand_ReturnsError() {
        // Arrange & Act
        var (reader, writer, handleTask, cleanup) = await SetupSessionAsync();
        using (cleanup) {

            // Read greeting and discard
            await reader.ReadLineAsync();

            // Send invalid command (just spaces)
            await writer.WriteLineAndFlushAsync("   ");

            // Read response
            var response = await reader.ReadLineAsync();

            // Assert
            Assert.Contains("-ERR Invalid command", response);

            // Cleanup
            await writer.WriteLineAndFlushAsync("QUIT");
            await handleTask;
        }
    }
}
