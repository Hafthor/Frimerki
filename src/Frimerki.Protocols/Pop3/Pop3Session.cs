using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Frimerki.Models.DTOs;
using Frimerki.Services.Message;
using Frimerki.Services.Session;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Pop3;

public partial class Pop3Session(
    ISessionService sessionService,
    IMessageService messageService,
    ILogger<Pop3Session> logger) {
    private NetworkStream _stream;
    private StreamReader _reader;
    private StreamWriter _writer;
    private string _username;
    private int? _userId;
    private List<MessageInfo> _messages = [];
    private readonly HashSet<int> _deletedMessages = [];

    [GeneratedRegex(@"^(\w+)(?:\s+(.+))?$")]
    private static partial Regex CommandRegex();

    public async Task HandleAsync(TcpClient client, CancellationToken cancellationToken) {
        try {
            _stream = client.GetStream();
            _reader = new StreamReader(_stream, Encoding.ASCII);
            _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

            await SendResponseAsync("+OK Frimerki POP3 Server Ready", cancellationToken);

            while (!cancellationToken.IsCancellationRequested) {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null) {
                    break;
                }

                logger.LogDebug("POP3 Command: {Command}", line);
                await ProcessCommandAsync(line, cancellationToken);
            }
        } catch (OperationCanceledException) {
            // Expected when cancellation token is triggered
        } catch (Exception ex) {
            logger.LogError(ex, "POP3 session error");
        } finally {
            // Clean up resources
            try {
                if (_writer != null) {
                    await _writer.DisposeAsync();
                }
            } catch {
                // Ignore disposal errors
            }
            _writer = null;
            try {
                _reader?.Dispose();
            } catch {
                // Ignore disposal errors
            }
            _reader = null;
            try {
                if (_stream != null) {
                    await _stream.DisposeAsync();
                }
            } catch {
                // Ignore disposal errors
            }
            _stream = null;
        }
    }

    private async Task ProcessCommandAsync(string command, CancellationToken cancellationToken) {
        var match = CommandRegex().Match(command);
        if (!match.Success) {
            await SendResponseAsync("-ERR Invalid command", cancellationToken);
            return;
        }

        var cmd = match.Groups[1].Value.ToUpperInvariant();
        var args = match.Groups[2].Value;

        try {
            await (cmd switch {
                "CAPA" => HandleCapaAsync(cancellationToken),
                "USER" => HandleUserAsync(args, cancellationToken),
                "PASS" => HandlePassAsync(args, cancellationToken),
                "STAT" => HandleStatAsync(cancellationToken),
                "LIST" => HandleListAsync(args, cancellationToken),
                "RETR" => HandleRetrAsync(args, cancellationToken),
                "DELE" => HandleDeleAsync(args, cancellationToken),
                "NOOP" => HandleNoopAsync(cancellationToken),
                "RSET" => HandleRsetAsync(cancellationToken),
                "QUIT" => HandleQuitAsync(cancellationToken),
                "UIDL" => HandleUidlAsync(args, cancellationToken),
                "TOP" => HandleTopAsync(args, cancellationToken),
                _ => SendResponseAsync("-ERR Unknown command", cancellationToken)
            });
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing POP3 command: {Command}", cmd);
            await SendResponseAsync("-ERR Server error", cancellationToken);
        }
    }

    private async Task HandleCapaAsync(CancellationToken cancellationToken) {
        await SendResponseAsync("+OK Capability list follows", cancellationToken);
        await SendResponseAsync("TOP", cancellationToken);
        await SendResponseAsync("UIDL", cancellationToken);
        await SendResponseAsync("USER", cancellationToken);
        await SendResponseAsync("RESP-CODES", cancellationToken);
        await SendResponseAsync("PIPELINING", cancellationToken);
        await SendResponseAsync(".", cancellationToken);
    }

    private async Task HandleUserAsync(string username, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(username)) {
            await SendResponseAsync("-ERR Username required", cancellationToken);
            return;
        }

        _username = username;
        await SendResponseAsync("+OK User accepted", cancellationToken);
    }

    private async Task HandlePassAsync(string password, CancellationToken cancellationToken) {
        if (_username == null) {
            await SendResponseAsync("-ERR Send USER command first", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(password)) {
            await SendResponseAsync("-ERR Password required", cancellationToken);
            return;
        }

        try {
            var loginResult = await sessionService.LoginAsync(new LoginRequest {
                Email = _username,
                Password = password
            });
            if (loginResult?.User == null) {
                await SendResponseAsync("-ERR Authentication failed", cancellationToken);
                return;
            }

            _userId = loginResult.User.Id;
            await LoadMessagesAsync(cancellationToken);

            var messageCount = _messages.Count;
            var totalSize = _messages.Sum(m => m.Size);

            await SendResponseAsync($"+OK {messageCount} messages ({totalSize} octets)", cancellationToken);
            logger.LogInformation("POP3 user {Username} authenticated successfully", _username);
        } catch (Exception ex) {
            logger.LogError(ex, "POP3 authentication error for user {Username}", _username);
            await SendResponseAsync("-ERR Authentication failed", cancellationToken);
        }
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken) {
        if (_userId == null) {
            return;
        }

        var request = new MessageFilterRequest {
            Folder = "INBOX",
            Skip = 0,
            Take = 1000 // POP3 typically loads all messages
        };

        var result = await messageService.GetMessagesAsync(_userId.Value, request);
        _messages = result.Items.Select((msg, index) => new MessageInfo(
            Index: index + 1,
            MessageId: msg.Id,
            Size: msg.MessageSize,
            Uid: msg.Id.ToString()
        )).ToList();
    }

    private async Task HandleStatAsync(CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        var visibleMessages = _messages.Where(m => !_deletedMessages.Contains(m.Index)).ToList();
        var totalSize = visibleMessages.Sum(m => m.Size);

        await SendResponseAsync($"+OK {visibleMessages.Count} {totalSize}", cancellationToken);
    }

    private async Task HandleListAsync(string args, CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(args)) {
            await ListAllMessagesAsync(cancellationToken);
        } else {
            await ListSpecificMessageAsync(args, cancellationToken);
        }
    }

    private async Task ListSpecificMessageAsync(string args, CancellationToken cancellationToken) {
        if (!int.TryParse(args, out var msgIndex)) {
            await SendResponseAsync("-ERR Invalid message number", cancellationToken);
            return;
        }

        var message = _messages.FirstOrDefault(m => m.Index == msgIndex);
        if (message == null || _deletedMessages.Contains(msgIndex)) {
            await SendResponseAsync("-ERR No such message", cancellationToken);
        } else {
            await SendResponseAsync($"+OK {msgIndex} {message.Size}", cancellationToken);
        }
    }

    private async Task ListAllMessagesAsync(CancellationToken cancellationToken) {
        var visibleMessages = _messages.Where(m => !_deletedMessages.Contains(m.Index)).ToList();
        await SendResponseAsync($"+OK {visibleMessages.Count} messages ({visibleMessages.Sum(m => m.Size)} octets)", cancellationToken);

        foreach (var msg in visibleMessages) {
            await SendResponseAsync($"{msg.Index} {msg.Size}", cancellationToken);
        }
        await SendResponseAsync(".", cancellationToken);
    }

    private async Task HandleRetrAsync(string args, CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        if (!int.TryParse(args, out var msgIndex)) {
            await SendResponseAsync("-ERR Invalid message number", cancellationToken);
            return;
        }

        var message = _messages.FirstOrDefault(m => m.Index == msgIndex);
        if (message == null || _deletedMessages.Contains(msgIndex)) {
            await SendResponseAsync("-ERR No such message", cancellationToken);
            return;
        }

        try {
            var messageResponse = await messageService.GetMessageAsync(_userId!.Value, message.MessageId);
            if (messageResponse == null) {
                await SendResponseAsync("-ERR Message not found", cancellationToken);
                return;
            }

            await SendResponseAsync($"+OK {message.Size} octets", cancellationToken);

            // Send headers
            await SendResponseAsync(messageResponse.Headers, cancellationToken);
            await SendResponseAsync("", cancellationToken); // Empty line between headers and body

            // Send body with dot-stuffing
            var bodyLines = messageResponse.Body.Split('\n');
            foreach (var line in bodyLines) {
                var lineToSend = line.StartsWith('.') ? "." + line : line;
                await SendResponseAsync(lineToSend, cancellationToken);
            }

            await SendResponseAsync(".", cancellationToken);
        } catch (Exception ex) {
            logger.LogError(ex, "Error retrieving message {MessageId}", message.MessageId);
            await SendResponseAsync("-ERR Error retrieving message", cancellationToken);
        }
    }

    private async Task HandleDeleAsync(string args, CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        if (!int.TryParse(args, out var msgIndex)) {
            await SendResponseAsync("-ERR Invalid message number", cancellationToken);
            return;
        }

        var message = _messages.FirstOrDefault(m => m.Index == msgIndex);
        if (message == null) {
            await SendResponseAsync("-ERR No such message", cancellationToken);
            return;
        }

        if (!_deletedMessages.Add(msgIndex)) {
            await SendResponseAsync("-ERR Message already deleted", cancellationToken);
            return;
        }

        await SendResponseAsync($"+OK Message {msgIndex} deleted", cancellationToken);
    }

    private async Task HandleNoopAsync(CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        await SendResponseAsync("+OK", cancellationToken);
    }

    private async Task HandleRsetAsync(CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        _deletedMessages.Clear();
        await SendResponseAsync("+OK", cancellationToken);
    }

    private async Task HandleQuitAsync(CancellationToken cancellationToken) {
        if (IsAuthenticated() && _userId.HasValue) {
            // Actually delete messages marked for deletion
            var deletedCount = 0;
            foreach (var messageId in _deletedMessages) {
                try {
                    var deleted = await messageService.DeleteMessageAsync(_userId.Value, messageId);
                    if (deleted) {
                        deletedCount++;
                    }
                } catch (Exception ex) {
                    logger.LogError(ex, "Error deleting message {MessageId} for user {UserId}", messageId, _userId);
                }
            }

            await SendResponseAsync($"+OK {deletedCount} messages deleted", cancellationToken);
        } else {
            await SendResponseAsync("+OK Bye", cancellationToken);
        }
    }

    private async Task HandleUidlAsync(string args, CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(args)) {
            await ListAllMessageUidsAsync(cancellationToken);
        } else {
            await GetSpecificMessageUidAsync(args, cancellationToken);
        }
    }

    private async Task GetSpecificMessageUidAsync(string args, CancellationToken cancellationToken) {
        // Get specific message UID
        if (!int.TryParse(args, out var msgIndex)) {
            await SendResponseAsync("-ERR Invalid message number", cancellationToken);
            return;
        }

        var message = _messages.FirstOrDefault(m => m.Index == msgIndex);
        if (message == null || _deletedMessages.Contains(msgIndex)) {
            await SendResponseAsync("-ERR No such message", cancellationToken);
        } else {
            await SendResponseAsync($"+OK {msgIndex} {message.Uid}", cancellationToken);
        }
    }

    private async Task ListAllMessageUidsAsync(CancellationToken cancellationToken) {
        // List all message UIDs
        var visibleMessages = _messages.Where(m => !_deletedMessages.Contains(m.Index)).ToList();
        await SendResponseAsync($"+OK {visibleMessages.Count} messages", cancellationToken);

        foreach (var msg in visibleMessages) {
            await SendResponseAsync($"{msg.Index} {msg.Uid}", cancellationToken);
        }
        await SendResponseAsync(".", cancellationToken);
    }

    private async Task HandleTopAsync(string args, CancellationToken cancellationToken) {
        if (!IsAuthenticated()) {
            await SendResponseAsync("-ERR Not authenticated", cancellationToken);
            return;
        }

        var parts = args.Split(' ');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var msgIndex) || !int.TryParse(parts[1], out var lineCount)) {
            await SendResponseAsync("-ERR Invalid arguments", cancellationToken);
            return;
        }

        var message = _messages.FirstOrDefault(m => m.Index == msgIndex);
        if (message == null || _deletedMessages.Contains(msgIndex)) {
            await SendResponseAsync("-ERR No such message", cancellationToken);
            return;
        }

        try {
            var messageResponse = await messageService.GetMessageAsync(_userId!.Value, message.MessageId);
            if (messageResponse == null) {
                await SendResponseAsync("-ERR Message not found", cancellationToken);
                return;
            }

            await SendResponseAsync($"+OK Headers and {lineCount} lines", cancellationToken);

            // Send headers
            await SendResponseAsync(messageResponse.Headers, cancellationToken);
            await SendResponseAsync("", cancellationToken); // Empty line between headers and body

            // Send specified number of body lines
            var bodyLines = messageResponse.Body.Split('\n');
            var linesToSend = Math.Min(lineCount, bodyLines.Length);

            for (int i = 0; i < linesToSend; i++) {
                var lineToSend = bodyLines[i].StartsWith('.') ? "." + bodyLines[i] : bodyLines[i];
                await SendResponseAsync(lineToSend, cancellationToken);
            }

            await SendResponseAsync(".", cancellationToken);
        } catch (Exception ex) {
            logger.LogError(ex, "Error retrieving message top {MessageId}", message.MessageId);
            await SendResponseAsync("-ERR Error retrieving message", cancellationToken);
        }
    }

    private async Task SendResponseAsync(string response, CancellationToken cancellationToken) {
        if (_writer != null) {
            await _writer.WriteLineAsync(response.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
            logger.LogDebug("POP3 Response: {Response}", response);
        }
    }

    private bool IsAuthenticated() => _userId.HasValue;

    private record MessageInfo(int Index, int MessageId, int Size, string Uid = "");
}
