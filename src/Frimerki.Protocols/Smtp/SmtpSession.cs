using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Frimerki.Models.Entities;
using Frimerki.Services.Email;
using Frimerki.Services.User;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Smtp;

/// <summary>
/// Handles individual SMTP client sessions following RFC 5321
/// </summary>
public partial class SmtpSession : IDisposable {
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private TcpClient _client;
    private NetworkStream _stream;
    private StreamReader _reader;
    private StreamWriter _writer;
    private readonly IUserService _userService;
    private readonly EmailDeliveryService _emailDeliveryService;
    private readonly ILogger _logger;

    private SmtpSessionState _state = SmtpSessionState.Initial;
    private User _authenticatedUser;
    private string _mailFrom;
    private readonly List<string> _rcptTo = [];
    private readonly StringBuilder _messageData = new();

    [GeneratedRegex(@"FROM:\s*<(.*)>", RegexOptions.IgnoreCase)]
    private static partial Regex MailFromRegex();

    [GeneratedRegex(@"TO:\s*<(.*)>", RegexOptions.IgnoreCase)]
    private static partial Regex RcptToRegex();

    public SmtpSession(TcpClient client, IUserService userService, EmailDeliveryService emailDeliveryService, ILogger logger) {
        _client = client;
        _stream = client.GetStream();
        _reader = new StreamReader(_stream, Utf8NoBom);
        _writer = new StreamWriter(_stream, Utf8NoBom) { AutoFlush = true };
        _userService = userService;
        _emailDeliveryService = emailDeliveryService;
        _logger = logger;
    }

    public async Task HandleAsync(CancellationToken cancellationToken) {
        try {
            // Send greeting
            await SendResponseAsync("220 frimerki.local ESMTP Frimerki Mail Server");
            _state = SmtpSessionState.Connected;

            string line;
            while (!cancellationToken.IsCancellationRequested &&
                   (line = await _reader.ReadLineAsync(cancellationToken)) != null) {

                try {
                    await ProcessCommandAsync(line.Trim(), cancellationToken);

                    if (_state == SmtpSessionState.Quit) {
                        break;
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error processing SMTP command: {Command}", line);
                    await SendResponseAsync("451 Requested action aborted: local error in processing");
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "SMTP session error");
        }
    }

    private async Task ProcessCommandAsync(string command, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(command)) {
            await SendResponseAsync("500 Syntax error, command unrecognized");
            return;
        }

        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToUpperInvariant();
        var args = parts.Length > 1 ? parts[1] : "";

        await (cmd switch {
            "HELO" => HandleHeloAsync(args),
            "EHLO" => HandleEhloAsync(args),
            "AUTH" => HandleAuthAsync(args, cancellationToken),
            "MAIL" => HandleMailFromAsync(args),
            "RCPT" => HandleRcptToAsync(args),
            "DATA" => HandleDataAsync(cancellationToken),
            "RSET" => HandleRsetAsync(),
            "NOOP" => SendResponseAsync("250 OK"),
            "QUIT" => HandleQuitAsync(),
            "HELP" => HandleHelpAsync(),
            _ => SendResponseAsync("500 Syntax error, command unrecognized")
        });
    }

    private async Task HandleHeloAsync(string hostname) {
        if (string.IsNullOrWhiteSpace(hostname)) {
            await SendResponseAsync("501 Syntax: HELO hostname");
            return;
        }

        _state = SmtpSessionState.Helo;
        await SendResponseAsync("250 frimerki.local Hello, pleased to meet you");
    }

    private async Task HandleEhloAsync(string hostname) {
        if (string.IsNullOrWhiteSpace(hostname)) {
            await SendResponseAsync("501 Syntax: EHLO hostname");
            return;
        }

        _state = SmtpSessionState.Ehlo;
        await SendResponseAsync("250-frimerki.local Hello, pleased to meet you");
        await SendResponseAsync("250-AUTH PLAIN LOGIN");
        await SendResponseAsync("250-8BITMIME");
        await SendResponseAsync("250 ENHANCEDSTATUSCODES");
    }

    private async Task HandleAuthAsync(string args, CancellationToken cancellationToken) {
        if (_state != SmtpSessionState.Ehlo && _state != SmtpSessionState.Helo) {
            await SendResponseAsync("503 Bad sequence of commands");
            return;
        }

        var parts = args.Split(' ', 2);
        var mechanism = parts[0].ToUpperInvariant();

        await (mechanism switch {
            "PLAIN" => HandleAuthPlainAsync(parts.Length > 1 ? parts[1] : null, cancellationToken),
            "LOGIN" => HandleAuthLoginAsync(cancellationToken),
            _ => SendResponseAsync("504 Authentication mechanism not supported")
        });
    }

    private async Task HandleAuthPlainAsync(string credentials, CancellationToken cancellationToken) {
        if (credentials == null) {
            await SendResponseAsync("334 ");
            credentials = await _reader.ReadLineAsync(cancellationToken);
            if (credentials == null) {
                await SendResponseAsync("501 Authentication cancelled");
                return;
            }
        }

        try {
            var decoded = Utf8NoBom.GetString(Convert.FromBase64String(credentials));
            var parts = decoded.Split('\0');

            if (parts.Length >= 3) {
                var username = parts[1];
                var password = parts[2];

                var user = await _userService.AuthenticateUserEntityAsync(username, password);
                if (user != null) {
                    _authenticatedUser = user;
                    _state = SmtpSessionState.Authenticated;
                    await SendResponseAsync("235 Authentication successful");
                    return;
                }
            }
        } catch {
            // Invalid base64 or other error
        }

        await SendResponseAsync("535 Authentication failed");
    }

    private async Task HandleAuthLoginAsync(CancellationToken cancellationToken) {
        await SendResponseAsync("334 VXNlcm5hbWU6"); // "Username:" in base64

        var username = await _reader.ReadLineAsync(cancellationToken);
        if (username == null) {
            await SendResponseAsync("501 Authentication cancelled");
            return;
        }

        await SendResponseAsync("334 UGFzc3dvcmQ6"); // "Password:" in base64

        var password = await _reader.ReadLineAsync(cancellationToken);
        if (password == null) {
            await SendResponseAsync("501 Authentication cancelled");
            return;
        }

        try {
            var decodedUsername = Utf8NoBom.GetString(Convert.FromBase64String(username));
            var decodedPassword = Utf8NoBom.GetString(Convert.FromBase64String(password));

            var user = await _userService.AuthenticateUserEntityAsync(decodedUsername, decodedPassword);
            if (user != null) {
                _authenticatedUser = user;
                _state = SmtpSessionState.Authenticated;
                await SendResponseAsync("235 Authentication successful");
                return;
            }
        } catch {
            // Invalid base64 or other error
        }

        await SendResponseAsync("535 Authentication failed");
    }

    private async Task HandleMailFromAsync(string args) {
        if (_state != SmtpSessionState.Ehlo && _state != SmtpSessionState.Helo &&
            _state != SmtpSessionState.Authenticated) {
            await SendResponseAsync("503 Bad sequence of commands");
            return;
        }

        var match = MailFromRegex().Match(args);
        if (!match.Success) {
            await SendResponseAsync("501 Syntax: MAIL FROM:<address>");
            return;
        }

        var fromAddress = match.Groups[1].Value;

        // TODO: Validate that authenticated user can send from this address
        _mailFrom = fromAddress;
        _rcptTo.Clear();
        _messageData.Clear();
        _state = SmtpSessionState.MailFrom;

        await SendResponseAsync("250 OK");
    }

    private async Task HandleRcptToAsync(string args) {
        if (_state != SmtpSessionState.MailFrom && _state != SmtpSessionState.RcptTo) {
            await SendResponseAsync("503 Bad sequence of commands");
            return;
        }

        var match = RcptToRegex().Match(args);
        if (!match.Success) {
            await SendResponseAsync("501 Syntax: RCPT TO:<address>");
            return;
        }

        var toAddress = match.Groups[1].Value;

        // TODO: Validate recipient address
        _rcptTo.Add(toAddress);
        _state = SmtpSessionState.RcptTo;

        await SendResponseAsync("250 OK");
    }

    private async Task HandleDataAsync(CancellationToken cancellationToken) {
        if (_state != SmtpSessionState.RcptTo) {
            await SendResponseAsync("503 Bad sequence of commands");
            return;
        }

        if (_rcptTo.Count == 0) {
            await SendResponseAsync("503 Valid RCPT TO required");
            return;
        }

        await SendResponseAsync("354 Start mail input; end with <CRLF>.<CRLF>");
        _state = SmtpSessionState.Data;

        string line;
        while ((line = await _reader.ReadLineAsync(cancellationToken)) != null) {
            if (line == ".") {
                // End of message
                await ProcessMessageAsync();
                return;
            }

            // Handle dot-stuffing (remove leading dot if line starts with ..)
            if (line.StartsWith("..")) {
                line = line[1..];
            }

            _messageData.AppendLine(line);
        }
    }

    private async Task ProcessMessageAsync() {
        try {
            var messageData = _messageData.ToString();

            _logger.LogInformation("Processing message from {From} to {Recipients}",
                _mailFrom, string.Join(", ", _rcptTo));

            // Deliver the message using the email delivery service
            var delivered = await _emailDeliveryService.DeliverEmailAsync(_mailFrom!, _rcptTo, messageData);

            if (delivered) {
                await SendResponseAsync("250 OK: Message accepted for delivery");
                _logger.LogInformation("Message delivered successfully from {From} to {Recipients}",
                    _mailFrom, string.Join(", ", _rcptTo));
            } else {
                await SendResponseAsync("550 Requested action not taken: mailbox unavailable");
                _logger.LogWarning("Message delivery failed from {From} to {Recipients}",
                    _mailFrom, string.Join(", ", _rcptTo));
            }

            // Reset for next message
            _mailFrom = null;
            _rcptTo.Clear();
            _messageData.Clear();
            _state = _authenticatedUser != null ? SmtpSessionState.Authenticated : SmtpSessionState.Ehlo;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing message");
            await SendResponseAsync("451 Requested action aborted: local error in processing");
        }
    }

    private async Task HandleRsetAsync() {
        _mailFrom = null;
        _rcptTo.Clear();
        _messageData.Clear();
        _state = _authenticatedUser != null ? SmtpSessionState.Authenticated :
                 _state == SmtpSessionState.Ehlo ? SmtpSessionState.Ehlo : SmtpSessionState.Helo;

        await SendResponseAsync("250 OK");
    }

    private async Task HandleQuitAsync() {
        await SendResponseAsync("221 frimerki.local Service closing transmission channel");
        _state = SmtpSessionState.Quit;
    }

    private async Task HandleHelpAsync() {
        await SendResponseAsync("214-This is Frimerki Mail Server");
        await SendResponseAsync("214-Commands supported:");
        await SendResponseAsync("214-  HELO EHLO AUTH MAIL RCPT DATA RSET NOOP QUIT HELP");
        await SendResponseAsync("214 End of HELP info");
    }

    private async Task SendResponseAsync(string response) {
        await _writer.WriteLineAsync(response);
        await _writer.FlushAsync(); // Ensure data is immediately sent
        _logger.LogDebug("SMTP > {Response}", response);
    }

    public void Dispose() {
        // Clean up resources
        try {
            _writer?.Dispose();
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
            _stream?.Dispose();
        } catch {
            // Ignore disposal errors
        }
        _stream = null;
        try {
            _client?.Dispose();
        } catch {
            // Ignore disposal errors
        }
        _client = null;
    }
}

/// <summary>
/// SMTP session states
/// </summary>
public enum SmtpSessionState {
    Initial,
    Connected,
    Helo,
    Ehlo,
    Authenticated,
    MailFrom,
    RcptTo,
    Data,
    Quit
}
