using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Protocols.Imap;
using Frimerki.Protocols.Pop3;
using Frimerki.Protocols.Smtp;
using Frimerki.Services.Email;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.Session;
using Frimerki.Services.User;
using Frimerki.Tests.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace Frimerki.Tests.Protocols;

/// <summary>
/// Simple XUnit logger provider for test output
/// </summary>
public class XUnitLoggerProvider(ITestOutputHelper output) : ILoggerProvider {
    public ILogger CreateLogger(string categoryName) {
        return new XUnitLogger(output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// Simple XUnit logger implementation
/// </summary>
public class XUnitLogger(ITestOutputHelper output, string categoryName) : ILogger {
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception exception, Func<TState, Exception, string> formatter) {
        var message = formatter(state, exception);
        output.WriteLine($"[{logLevel}] {categoryName}: {message}");
        if (exception != null) {
            output.WriteLine(exception.ToString());
        }
    }

    private class NoOpDisposable : IDisposable {
        public void Dispose() { }
    }
}

[Collection("ConcurrentProtocolTests")]
public class ConcurrentProtocolTests(ITestOutputHelper output) {
    [Fact]
    public async Task ImapServer_CanHandleMultipleConcurrentConnections() {
        // Arrange
        const int connectionCount = 5;
        var port = TestPortProvider.GetNextPort();
        var results = new ConcurrentBag<string>();
        var server = CreateImapServer(port);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await server.StartAsync(cts.Token);
        var cancellationToken = cts.Token;

        // Act - Create multiple concurrent connections
        var tasks = new List<Task>();
        for (int i = 0; i < connectionCount; i++) {
            int connectionId = i;
            tasks.Add(Task.Run(async () => {
                try {
                    using var client = new TcpClient();
                    await client.ConnectAsync("127.0.0.1", port, cancellationToken);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII);
                    await using var writer = new StreamWriter(stream, Encoding.ASCII);
                    writer.AutoFlush = true;

                    // Read greeting
                    var greeting = await reader.ReadLineAsync(cancellationToken);
                    output.WriteLine($"Connection {connectionId}: {greeting}");

                    // Send unique capability request with connection ID
                    await writer.WriteLineAsync($"A{connectionId:D3} CAPABILITY");

                    // Read capability response
                    var response = new StringBuilder();
                    string line;
                    while ((line = await reader.ReadLineAsync(cancellationToken)) != null && !line.StartsWith($"A{connectionId:D3} OK")) {
                        response.AppendLine(line);
                    }
                    response.AppendLine(line); // Include the OK line

                    var fullResponse = response.ToString();
                    results.Add($"Connection-{connectionId}:{fullResponse}");

                    // Logout
                    await writer.WriteLineAsync($"A{connectionId:D3} LOGOUT");
                    await reader.ReadLineAsync(cancellationToken); // Read logout response
                } catch (Exception ex) {
                    output.WriteLine($"Connection {connectionId} failed: {ex.Message}");
                }
            }, cts.Token));
        }

        await Task.WhenAll(tasks);
        await cts.CancelAsync();

        // Assert
        Assert.Equal(connectionCount, results.Count);

        // Verify each connection got unique responses
        var responsesByConnection = results.ToList();
        for (int i = 0; i < connectionCount; i++) {
            var expectedPrefix = $"Connection-{i}:";
            var connectionResponse = responsesByConnection.FirstOrDefault(r => r.StartsWith(expectedPrefix));
            Assert.NotNull(connectionResponse);

            // Verify the response contains the correct tag
            Assert.Contains($"A{i:D3} OK", connectionResponse);
        }
    }

    [Fact]
    public async Task Pop3Server_CanHandleMultipleConcurrentConnections() {
        // Arrange
        const int connectionCount = 5;
        var results = new ConcurrentBag<string>();

        var tasks = new List<Task>();
        for (int i = 0; i < connectionCount; i++) {
            int connectionId = i;
            tasks.Add(Task.Run(async () => {
                try {
                    var (reader, writer, handleTask, cleanup) = await SetupPop3SessionAsync();
                    using (cleanup) {
                        // Read greeting
                        var greeting = await reader.ReadLineAsync();
                        output.WriteLine($"POP3 Connection {connectionId}: {greeting}");

                        // Send unique USER command with connection-specific username
                        await writer.WriteLineAndFlushAsync($"USER test{connectionId}@example.com");
                        var userResponse = await reader.ReadLineAsync();

                        // Send PASS command
                        await writer.WriteLineAndFlushAsync("PASS password");
                        var passResponse = await reader.ReadLineAsync();

                        // Send NOOP to test authenticated state
                        await writer.WriteLineAndFlushAsync("NOOP");
                        var noopResponse = await reader.ReadLineAsync();

                        results.Add($"Connection-{connectionId}:USER={userResponse}|PASS={passResponse}|NOOP={noopResponse}");

                        // Cleanup
                        try {
                            await writer.WriteLineAndFlushAsync("QUIT");
                            await handleTask;
                        } catch (IOException) {
                            // Connection may be closed
                        }
                    }
                } catch (Exception ex) {
                    output.WriteLine($"POP3 Connection {connectionId} failed: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(connectionCount, results.Count);

        // Verify each connection got unique responses
        for (int i = 0; i < connectionCount; i++) {
            var expectedPrefix = $"Connection-{i}:";
            var connectionResponse = results.FirstOrDefault(r => r.StartsWith(expectedPrefix));
            Assert.NotNull(connectionResponse);
            output.WriteLine($"POP3 Result {i}: {connectionResponse}");
        }
    }

    [Fact]
    public async Task SmtpServer_CanHandleMultipleConcurrentConnections() {
        // Arrange
        const int connectionCount = 5;
        var results = new ConcurrentBag<string>();

        var tasks = new List<Task>();
        for (int i = 0; i < connectionCount; i++) {
            int connectionId = i;
            tasks.Add(Task.Run(async () => {
                try {
                    var (reader, writer, handleTask, cleanup) = await SetupSmtpSessionAsync();
                    using (cleanup) {
                        // Read greeting
                        var greeting = await reader.ReadLineAsync();
                        output.WriteLine($"SMTP Connection {connectionId}: {greeting}");

                        // Send unique EHLO command with connection-specific hostname
                        await writer.WriteLineAndFlushAsync($"EHLO client{connectionId}.example.com");

                        // Read EHLO response (multi-line)
                        var ehloResponse = new StringBuilder();
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null && !line.StartsWith("250 ")) {
                            ehloResponse.AppendLine(line);
                        }
                        ehloResponse.AppendLine(line); // Include the final 250 line

                        // Send NOOP
                        await writer.WriteLineAndFlushAsync("NOOP");
                        var noopResponse = await reader.ReadLineAsync();

                        results.Add($"Connection-{connectionId}:EHLO_FINAL={line}|NOOP={noopResponse}");

                        // Cleanup
                        try {
                            await writer.WriteLineAndFlushAsync("QUIT");
                            await handleTask;
                        } catch (IOException) {
                            // Connection may be closed
                        }
                    }
                } catch (Exception ex) {
                    output.WriteLine($"SMTP Connection {connectionId} failed: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(connectionCount, results.Count);

        // Verify each connection got unique responses
        for (int i = 0; i < connectionCount; i++) {
            var expectedPrefix = $"Connection-{i}:";
            var connectionResponse = results.FirstOrDefault(r => r.StartsWith(expectedPrefix));
            Assert.NotNull(connectionResponse);
            output.WriteLine($"SMTP Result {i}: {connectionResponse}");
        }
    }

    private ImapServer CreateImapServer(int port) {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => {
            builder.AddProvider(new XUnitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Mock services
        var mockUserService = new Mock<IUserService>();
        var mockFolderService = new Mock<IFolderService>();
        var mockMessageService = new Mock<IMessageService>();

        services.AddSingleton(mockUserService.Object);
        services.AddSingleton(mockFolderService.Object);
        services.AddSingleton(mockMessageService.Object);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<ImapServer>>();
        return new ImapServer(logger, serviceProvider, port);
    }

    private async Task<(StreamReader reader, StreamWriter writer, Task handleTask, IDisposable cleanup)> SetupPop3SessionAsync() {
        // Create fresh TCP connections for each test
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = listener.LocalEndpoint as System.Net.IPEndPoint;

        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, endpoint!.Port);
        var stream = client.GetStream();

        var serverClient = await listener.AcceptTcpClientAsync();

        // Setup mocks
        var mockSessionService = new Mock<ISessionService>();
        var mockMessageService = new Mock<IMessageService>();
        var mockLogger = new Mock<ILogger<Pop3Session>>();

        // Setup authentication for any test user
        var loginResponse = new LoginResponse {
            User = new UserSessionInfo { Id = 1, Username = "test@example.com", Email = "test@example.com" },
            Token = "test-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        mockSessionService.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>()))
                          .ReturnsAsync(loginResponse);

        var session = new Pop3Session(mockSessionService.Object, mockMessageService.Object, mockLogger.Object);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5000));
        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true };

        var handleTask = session.HandleAsync(serverClient, cts.Token);

        // Return cleanup delegate
        var cleanup = new DisposableAction(() => {
            reader.Dispose();
            writer.Dispose();
            stream.Dispose();
            client.Dispose();
            serverClient.Dispose();
            listener.Stop();
        });

        return (reader, writer, handleTask, cleanup);
    }

    private async Task<(StreamReader reader, StreamWriter writer, Task handleTask, IDisposable cleanup)> SetupSmtpSessionAsync() {
        // Create fresh TCP connections for each test
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = listener.LocalEndpoint as System.Net.IPEndPoint;

        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, endpoint!.Port);
        var stream = client.GetStream();

        var serverClient = await listener.AcceptTcpClientAsync();

        // Setup mocks - SMTP needs different services
        var mockUserService = new Mock<IUserService>();
        var mockLogger = new Mock<ILogger>();

        // Create in-memory database context for EmailDeliveryService
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new EmailDbContext(options);
        var nowProvider = new MockNowProvider();
        var mockEdsLogger = new Mock<ILogger<EmailDeliveryService>>();
        var emailDeliveryService = new EmailDeliveryService(context, mockUserService.Object, nowProvider, mockEdsLogger.Object);

        var session = new SmtpSession(serverClient, mockUserService.Object, emailDeliveryService, mockLogger.Object);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5000));
        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true };

        var handleTask = session.HandleAsync(cts.Token);

        // Return cleanup delegate
        var cleanup = new DisposableAction(() => {
            reader.Dispose();
            writer.Dispose();
            stream.Dispose();
            client.Dispose();
            serverClient.Dispose();
            listener.Stop();
            context.Dispose();
        });

        return (reader, writer, handleTask, cleanup);
    }

    private class DisposableAction(Action action) : IDisposable {
        public void Dispose() => action();
    }
}

file static class ExtensionMethods {
    public static async Task WriteLineAndFlushAsync(this StreamWriter writer, string message) {
        await writer.WriteLineAsync(message);
        await writer.FlushAsync();
    }
}
