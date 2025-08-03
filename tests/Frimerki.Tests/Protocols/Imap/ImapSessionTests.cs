using System.Net.Sockets;
using System.Text;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Imap;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace Frimerki.Tests.Protocols.Imap;

public class ImapCommandParserTests {
    [Fact]
    public void ParseCommand_ValidCommand_ReturnsCorrectCommand() {
        // Arrange
        var commandLine = "a1 CAPABILITY";

        // Act
        var command = ImapCommandParser.ParseCommand(commandLine);

        // Assert
        Assert.NotNull(command);
        Assert.Equal("a1", command.Tag);
        Assert.Equal("CAPABILITY", command.Name);
        Assert.Empty(command.Arguments);
        Assert.Equal(commandLine, command.RawCommand);
    }

    [Fact]
    public void ParseCommand_LoginCommand_ParsesArgumentsCorrectly() {
        // Arrange
        var commandLine = "a2 LOGIN \"test@example.com\" \"password\"";

        // Act
        var command = ImapCommandParser.ParseCommand(commandLine);

        // Assert
        Assert.NotNull(command);
        Assert.Equal("a2", command.Tag);
        Assert.Equal("LOGIN", command.Name);
        Assert.Equal(2, command.Arguments.Count);
        Assert.Equal("test@example.com", command.Arguments[0]); // Parser removes quotes
        Assert.Equal("password", command.Arguments[1]); // Parser removes quotes
    }

    [Fact]
    public void ParseCommand_ListCommand_ParsesArgumentsCorrectly() {
        // Arrange
        var commandLine = "a3 LIST \"\" \"*\"";

        // Act
        var command = ImapCommandParser.ParseCommand(commandLine);

        // Assert
        Assert.NotNull(command);
        Assert.Equal("a3", command.Tag);
        Assert.Equal("LIST", command.Name);
        Assert.Single(command.Arguments); // Parser skips empty strings, only "*" remains
        Assert.Equal("*", command.Arguments[0]);
    }

    [Fact]
    public void ParseCommand_SelectCommand_ParsesArgumentsCorrectly() {
        // Arrange
        var commandLine = "a4 SELECT \"INBOX\"";

        // Act
        var command = ImapCommandParser.ParseCommand(commandLine);

        // Assert
        Assert.NotNull(command);
        Assert.Equal("a4", command.Tag);
        Assert.Equal("SELECT", command.Name);
        Assert.Single(command.Arguments);
        Assert.Equal("INBOX", command.Arguments[0]); // Parser removes quotes
    }

    [Fact]
    public void ParseCommand_FetchCommand_ParsesArgumentsCorrectly() {
        // Arrange
        var commandLine = "a5 FETCH 1:5 (FLAGS BODY[HEADER])";

        // Act
        var command = ImapCommandParser.ParseCommand(commandLine);

        // Assert
        Assert.NotNull(command);
        Assert.Equal("a5", command.Tag);
        Assert.Equal("FETCH", command.Name);
        Assert.Equal(3, command.Arguments.Count); // Parser splits on spaces
        Assert.Equal("1:5", command.Arguments[0]);
        Assert.Equal("(FLAGS", command.Arguments[1]);
        Assert.Equal("BODY[HEADER])", command.Arguments[2]);
    }

    [Fact]
    public void ParseCommand_EmptyOrWhitespace_ReturnsNull() {
        // Test empty string
        Assert.Null(ImapCommandParser.ParseCommand(""));

        // Test whitespace only
        Assert.Null(ImapCommandParser.ParseCommand("   "));

        // Test null
        Assert.Null(ImapCommandParser.ParseCommand(null));
    }

    [Fact]
    public void ParseCommand_InsufficientParts_ReturnsNull() {
        // Arrange - command with only tag, no command name
        var commandLine = "a1";

        // Act
        var command = ImapCommandParser.ParseCommand(commandLine);

        // Assert
        Assert.Null(command);
    }

    [Fact]
    public void UnquoteString_QuotedString_RemovesQuotes() {
        // Arrange
        var quotedString = "\"test@example.com\"";

        // Act
        var unquoted = ImapCommandParser.UnquoteString(quotedString);

        // Assert
        Assert.Equal("test@example.com", unquoted);
    }

    [Fact]
    public void UnquoteString_UnquotedString_ReturnsAsIs() {
        // Arrange
        var unquotedString = "test@example.com";

        // Act
        var result = ImapCommandParser.UnquoteString(unquotedString);

        // Assert
        Assert.Equal("test@example.com", result);
    }

    [Fact]
    public void UnquoteString_EmptyOrMalformed_ReturnsAsIs() {
        // Test empty string
        Assert.Equal("", ImapCommandParser.UnquoteString(""));

        // Test single quote
        Assert.Equal("\"", ImapCommandParser.UnquoteString("\""));

        // Test malformed (missing closing quote)
        Assert.Equal("\"test", ImapCommandParser.UnquoteString("\"test"));
    }
}

public class ImapTypesTests {
    [Fact]
    public void ImapResponse_ToString_FormatsCorrectly() {
        // Arrange
        var response = new ImapResponse {
            Tag = "a1",
            Type = ImapResponseType.Ok,
            Message = "CAPABILITY completed"
        };

        // Act
        var result = response.ToString();

        // Assert
        Assert.Equal("a1 OK CAPABILITY completed", result);
    }

    [Fact]
    public void ImapResponse_ToString_WithResponseCode_FormatsCorrectly() {
        // Arrange
        var response = new ImapResponse {
            Tag = "a2",
            Type = ImapResponseType.Ok,
            Message = "SELECT completed",
            ResponseCode = "READ-WRITE"
        };

        // Act
        var result = response.ToString();

        // Assert
        Assert.Equal("a2 OK [READ-WRITE] SELECT completed", result);
    }

    [Fact]
    public void ImapCommand_DefaultValues_AreSetCorrectly() {
        // Arrange & Act
        var command = new ImapCommand();

        // Assert
        Assert.Equal("", command.Tag);
        Assert.Equal("", command.Name);
        Assert.Empty(command.Arguments);
        Assert.Equal("", command.RawCommand);
    }

    [Theory]
    [InlineData(ImapConnectionState.NotAuthenticated)]
    [InlineData(ImapConnectionState.Authenticated)]
    [InlineData(ImapConnectionState.Selected)]
    [InlineData(ImapConnectionState.Logout)]
    public void ImapConnectionState_AllValuesAreValid(ImapConnectionState state) {
        // This test ensures all enum values are defined and accessible
        Assert.True(Enum.IsDefined(typeof(ImapConnectionState), state));
    }

    [Theory]
    [InlineData(ImapResponseType.Ok)]
    [InlineData(ImapResponseType.No)]
    [InlineData(ImapResponseType.Bad)]
    [InlineData(ImapResponseType.Bye)]
    [InlineData(ImapResponseType.Preauth)]
    public void ImapResponseType_AllValuesAreValid(ImapResponseType responseType) {
        // This test ensures all enum values are defined and accessible
        Assert.True(Enum.IsDefined(typeof(ImapResponseType), responseType));

        // Also test that the enum value can be converted to string
        var stringValue = responseType.ToString();
        Assert.NotEmpty(stringValue);
    }
}

public sealed class ImapStreamBasedTests : IDisposable {
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly Mock<ILogger<ImapSession>> _mockLogger = new();
    private readonly Mock<IUserService> _mockUserService = new();
    private readonly Mock<IFolderService> _mockFolderService = new();
    private readonly Mock<IMessageService> _mockMessageService = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public void Dispose() {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private async Task<(TcpClient client, NetworkStream stream, Task sessionTask, TcpListener listener)> SetupImapSessionAsync() {
        // Create a new listener for each test to avoid port conflicts
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = listener.LocalEndpoint as System.Net.IPEndPoint;
        var clientTask = ConnectClientAsync(endpoint!.Port);
        var serverTask = AcceptServerConnectionAsync(listener);

        var client = await clientTask;
        var serverClient = await serverTask;
        var stream = client.GetStream();

        var session = new ImapSession(
            serverClient,
            _mockLogger.Object,
            _mockUserService.Object,
            _mockFolderService.Object,
            _mockMessageService.Object);

        var sessionTask = session.HandleSessionAsync();
        return (client, stream, sessionTask, listener);
    }

    private async Task<TcpClient> ConnectClientAsync(int port) {
        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        return client;
    }

    private async Task<TcpClient> AcceptServerConnectionAsync(TcpListener listener) {
        return await listener.AcceptTcpClientAsync();
    }

    private async Task SendCommandAsync(NetworkStream stream, string command) {
        var commandBytes = Utf8NoBom.GetBytes(command + "\r\n");
        await stream.WriteAsync(commandBytes);
        await stream.FlushAsync();
    }

    private async Task<string[]> ReadResponseAsync(NetworkStream stream) {
        var buffer = new byte[4096];
        List<string> responses = [];

        // Read with timeout to handle potential multiple responses
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try {
            var bytesRead = await stream.ReadAsync(buffer, cts.Token);
            var response = Utf8NoBom.GetString(buffer, 0, bytesRead);
            var lines = response.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries);
            responses.AddRange(lines);
        } catch (OperationCanceledException) {
            // Timeout - return what we have
        }

        return responses.ToArray();
    }

    [Fact]
    public async Task ImapSession_SendsGreetingOnConnection() {
        // Arrange & Act
        var (client, stream, sessionTask, listener) = await SetupImapSessionAsync();

        // Read the greeting - use raw bytes to match IMAP protocol
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer);
        var greeting = Utf8NoBom.GetString(buffer, 0, bytesRead).Trim();

        // Assert
        Assert.NotNull(greeting);
        Assert.Contains("* OK", greeting);
        Assert.Contains("CAPABILITY IMAP4rev1", greeting);
        Assert.Contains("Frimerki IMAP Server ready", greeting);

        // Cleanup
        var logoutBytes = Utf8NoBom.GetBytes("a1 LOGOUT\r\n");
        await stream.WriteAsync(logoutBytes);
        await stream.FlushAsync();
        client.Close();
        listener.Stop();

        try {
            await sessionTask.WaitAsync(TimeSpan.FromSeconds(1));
        } catch (TimeoutException) {
            // Session may not complete cleanly in test environment
        }
    }

    [Fact]
    public async Task ImapSession_CapabilityCommand_ReturnsCorrectCapabilities() {
        // Arrange
        var (client, stream, sessionTask, listener) = await SetupImapSessionAsync();

        // Read greeting
        await ReadResponseAsync(stream);

        // Act - Send CAPABILITY command
        await SendCommandAsync(stream, "a1 CAPABILITY");

        // Assert - Read capability responses (might come together or separately)
        var responses = await ReadResponseAsync(stream);

        if (responses.Length == 1) {
            // Responses came separately
            Assert.StartsWith("* CAPABILITY", responses[0]);
            Assert.Contains("IMAP4rev1", responses[0]);
            Assert.Contains("STARTTLS", responses[0]);
            Assert.Contains("AUTH=PLAIN", responses[0]);

            var okResponse = await ReadResponseAsync(stream);
            Assert.Single(okResponse);
            Assert.Equal("a1 OK CAPABILITY completed", okResponse[0]);
        } else if (responses.Length == 2) {
            // Both responses came together
            Assert.StartsWith("* CAPABILITY", responses[0]);
            Assert.Contains("IMAP4rev1", responses[0]);
            Assert.Contains("STARTTLS", responses[0]);
            Assert.Contains("AUTH=PLAIN", responses[0]);
            Assert.Equal("a1 OK CAPABILITY completed", responses[1]);
        } else {
            Assert.Fail($"Unexpected number of responses: {responses.Length}");
        }

        // Cleanup
        await SendCommandAsync(stream, "a2 LOGOUT");
        client.Close();
        listener.Stop();

        try {
            await sessionTask.WaitAsync(TimeSpan.FromSeconds(1));
        } catch (TimeoutException) {
            // Session may not complete cleanly in test environment
        }
    }

    [Fact]
    public async Task ImapSession_NoopCommand_ReturnsOk() {
        // Arrange
        var (client, stream, sessionTask, listener) = await SetupImapSessionAsync();

        // Read greeting
        await ReadResponseAsync(stream);

        // Act - Send NOOP command
        await SendCommandAsync(stream, "a1 NOOP");

        // Assert - Read response
        var responses = await ReadResponseAsync(stream);

        Assert.Single(responses);
        Assert.Equal("a1 OK NOOP completed", responses[0]);

        // Cleanup
        await SendCommandAsync(stream, "a2 LOGOUT");
        client.Close();
        listener.Stop();

        try {
            await sessionTask.WaitAsync(TimeSpan.FromSeconds(1));
        } catch (TimeoutException) {
            // Session may not complete cleanly in test environment
        }
    }

    [Fact]
    public async Task ImapSession_LoginCommand_WithValidCredentials_Succeeds() {
        // Arrange
        var testUser = new User { Id = 1, Username = "test@example.com" };
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("test@example.com", "password"))
                       .ReturnsAsync(testUser);

        var (client, stream, sessionTask, listener) = await SetupImapSessionAsync();

        // Read greeting
        await ReadResponseAsync(stream);

        // Act - Send LOGIN command
        await SendCommandAsync(stream, "a1 LOGIN \"test@example.com\" \"password\"");

        // Assert - Read login response
        var responses = await ReadResponseAsync(stream);

        Assert.Single(responses);
        Assert.Equal("a1 OK LOGIN completed", responses[0]);

        // Cleanup
        await SendCommandAsync(stream, "a2 LOGOUT");
        client.Close();
        listener.Stop();

        try {
            await sessionTask.WaitAsync(TimeSpan.FromSeconds(1));
        } catch (TimeoutException) {
            // Session may not complete cleanly in test environment
        }
    }

    [Fact]
    public async Task ImapSession_LogoutCommand_SendsByeAndOk() {
        // Arrange
        var (client, stream, sessionTask, listener) = await SetupImapSessionAsync();

        // Read greeting
        await ReadResponseAsync(stream);

        // Act - Send LOGOUT command
        await SendCommandAsync(stream, "a1 LOGOUT");

        // Assert - Read logout responses (might come together or separately)
        var responses = await ReadResponseAsync(stream);

        if (responses.Length == 1) {
            // Responses came separately
            Assert.StartsWith("* BYE", responses[0]);
            Assert.Contains("logging out", responses[0]);

            var okResponse = await ReadResponseAsync(stream);
            Assert.Single(okResponse);
            Assert.Equal("a1 OK LOGOUT completed", okResponse[0]);
        } else if (responses.Length == 2) {
            // Both responses came together
            Assert.StartsWith("* BYE", responses[0]);
            Assert.Contains("logging out", responses[0]);
            Assert.Equal("a1 OK LOGOUT completed", responses[1]);
        } else {
            Assert.Fail($"Unexpected number of responses: {responses.Length}");
        }

        client.Close();
        listener.Stop();

        // Session should complete after LOGOUT
        await sessionTask.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
