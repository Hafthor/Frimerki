using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Common;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// Handles individual IMAP client connections
/// </summary>
public partial class ImapSession(
    TcpClient client,
    ILogger<ImapSession> logger,
    IUserService userService,
    IMessageService messageService) {
    private TcpClient _client = client;
    private readonly NetworkStream _stream = client.GetStream();

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    // Unified map of standard IMAP flag names to their getter (response) and setter (request) accessors.
    // \Recent is read-only per RFC 3501 so it has no setter.
    private static readonly Dictionary<string, (Func<MessageFlagsResponse, bool> Get, Action<MessageFlagsRequest, bool?> Set)>
        StandardFlags = new(StringComparer.OrdinalIgnoreCase) {
            [@"\Seen"] = (f => f.Seen, (r, v) => r.Seen = v),
            [@"\Answered"] = (f => f.Answered, (r, v) => r.Answered = v),
            [@"\Flagged"] = (f => f.Flagged, (r, v) => r.Flagged = v),
            [@"\Deleted"] = (f => f.Deleted, (r, v) => r.Deleted = v),
            [@"\Draft"] = (f => f.Draft, (r, v) => r.Draft = v),
            [@"\Recent"] = (f => f.Recent, null),
        };

    public ImapConnectionState State { get; private set; } = ImapConnectionState.NotAuthenticated;
    public User CurrentUser { get; private set; }
    public string SelectedFolder { get; private set; }
    public bool IsReadOnly { get; private set; }

    private string _pendingCommandTail;

    // --- Response helpers ---

    private static ImapResponse OkResponse(ImapCommand cmd, string message) =>
        new(cmd.Tag, ImapResponseType.Ok, message);

    private static ImapResponse NoResponse(ImapCommand cmd, string message) =>
        new(cmd.Tag, ImapResponseType.No, message);

    private static ImapResponse BadResponse(ImapCommand cmd, string message) =>
        new(cmd.Tag, ImapResponseType.Bad, message);

    // --- State guard helpers ---

    private bool RequiresAuth =>
        State != ImapConnectionState.Authenticated && State != ImapConnectionState.Selected;

    private bool RequiresSelected => State != ImapConnectionState.Selected;

    public async Task HandleSessionAsync() {
        var readBuffer = ArrayPool<byte>.Shared.Rent(8192);
        try {
            logger.LogInformation("IMAP: Starting session for client");
            await SendResponseAsync("* OK [CAPABILITY IMAP4rev1 STARTTLS AUTH=PLAIN UIDPLUS] Frimerki IMAP Server ready");

            while (_client.Connected && State != ImapConnectionState.Logout) {
                var bytesRead = await _stream.ReadAsync(readBuffer);
                if (bytesRead == 0) {
                    logger.LogInformation("IMAP: Client disconnected (no more data)");
                    break;
                }

                var lineEndIndex = Array.IndexOf(readBuffer, (byte)'\n', 0, bytesRead);
                if (lineEndIndex < 0) {
                    _pendingCommandTail = (_pendingCommandTail ?? "") + Utf8NoBom.GetString(readBuffer, 0, bytesRead);
                    continue;
                }

                var commandLine = Utf8NoBom.GetString(readBuffer, 0, lineEndIndex).TrimEnd('\r');
                if (_pendingCommandTail != null) {
                    commandLine = _pendingCommandTail + commandLine;
                    _pendingCommandTail = null;
                }

                if (commandLine.Length == 0) {
                    continue;
                }

                if (commandLine.Contains("APPEND") && commandLine.Contains('{')) {
                    var literalStart = lineEndIndex + 1;
                    var initialLiteral = bytesRead > literalStart
                        ? new ReadOnlyMemory<byte>(readBuffer, literalStart, bytesRead - literalStart)
                        : ReadOnlyMemory<byte>.Empty;
                    await HandleAppendCommandWithLiteral(commandLine, initialLiteral);
                } else {
                    await ProcessCommandAsync(commandLine);
                    if (bytesRead > lineEndIndex + 1) {
                        await ProcessExtraCommandBytes(
                            new ReadOnlyMemory<byte>(readBuffer, lineEndIndex + 1, bytesRead - lineEndIndex - 1));
                    }
                }
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error in IMAP session");
        } finally {
            ArrayPool<byte>.Shared.Return(readBuffer);
            try { _client?.Dispose(); } catch { /* Ignore cleanup errors */ }
            _client = null;
        }
    }

    private async Task ProcessExtraCommandBytes(ReadOnlyMemory<byte> extraBytes) {
        if (extraBytes.IsEmpty) {
            return;
        }

        var extraText = Utf8NoBom.GetString(extraBytes.Span);
        var searchFrom = 0;

        while (searchFrom < extraText.Length) {
            var newlineIndex = extraText.IndexOf('\n', searchFrom);
            if (newlineIndex < 0) {
                _pendingCommandTail = (_pendingCommandTail ?? "") + extraText[searchFrom..];
                break;
            }

            var lineEnd = newlineIndex;
            if (lineEnd > searchFrom && extraText[lineEnd - 1] == '\r') {
                lineEnd--;
            }

            var trimmed = extraText[searchFrom..lineEnd];
            searchFrom = newlineIndex + 1;

            if (trimmed.Length > 0) {
                await ProcessCommandAsync(trimmed);
            }
        }
    }

    private async Task ProcessCommandAsync(string commandLine) {
        try {
            logger.LogInformation("IMAP Command: {Command}", commandLine);

            if (commandLine.Contains("APPEND") && commandLine.Contains('{')) {
                await HandleAppendCommandWithLiteral(commandLine);
                return;
            }

            var command = ImapCommandParser.ParseCommand(commandLine);
            if (command == null) {
                logger.LogWarning("IMAP: Failed to parse command: {Command}", commandLine);
                await SendResponseAsync("* BAD Invalid command syntax");
                return;
            }

            var response = command.Name switch {
                "CAPABILITY" => await HandleCapabilityAsync(command),
                "LOGIN" => await HandleLoginAsync(command),
                "AUTHENTICATE" => await HandleAuthenticateAsync(command),
                "LOGOUT" => await HandleLogoutAsync(command),
                "NOOP" => await HandleNoopAsync(command),
                "SELECT" => await HandleSelectExamineAsync(command, false),
                "EXAMINE" => await HandleSelectExamineAsync(command, true),
                "LIST" => await HandleListAsync(command),
                "APPEND" => await HandleAppendAsync(command),
                "FETCH" => await HandleFetchAsync(command),
                "SEARCH" => await HandleSearchAsync(command),
                "STORE" => await HandleStoreAsync(command),
                "EXPUNGE" => await HandleExpungeAsync(command),
                _ => BadResponse(command, "Unknown command")
            };

            await SendResponseAsync($"{response.Tag} {response.Type.ToString().ToUpper()} {response.Message}");
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing command: {Command}", commandLine);
            await SendResponseAsync("* BAD Internal server error");
        }
    }

    private async Task<ImapResponse> HandleCapabilityAsync(ImapCommand command) {
        await SendResponseAsync("* CAPABILITY IMAP4rev1 STARTTLS AUTH=PLAIN UIDPLUS");
        return OkResponse(command, "CAPABILITY completed");
    }

    private async Task<ImapResponse> HandleLoginAsync(ImapCommand command) {
        if (State != ImapConnectionState.NotAuthenticated) {
            return BadResponse(command, "Already authenticated");
        }

        if (command.Arguments.Count < 2) {
            return BadResponse(command, "LOGIN requires username and password");
        }

        var username = ImapCommandParser.UnquoteString(command.Arguments[0]);
        var password = ImapCommandParser.UnquoteString(command.Arguments[1]);

        try {
            var user = await userService.AuthenticateUserEntityAsync(username, password);
            if (user != null) {
                CurrentUser = user;
                State = ImapConnectionState.Authenticated;
                return OkResponse(command, "LOGIN completed");
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "Login failed for user {Username}", username);
        }

        return NoResponse(command, "LOGIN failed");
    }

    private async Task<ImapResponse> HandleAuthenticateAsync(ImapCommand command) {
        if (State != ImapConnectionState.NotAuthenticated) {
            return BadResponse(command, "Already authenticated");
        }

        if (command.Arguments.Count < 1) {
            return BadResponse(command, "AUTHENTICATE requires mechanism");
        }

        if (!command.Arguments[0].Equals("PLAIN", StringComparison.OrdinalIgnoreCase)) {
            return NoResponse(command, "Authentication mechanism not supported");
        }

        await SendResponseAsync("+ ");

        // Read PLAIN auth data from client
        var authBuffer = ArrayPool<byte>.Shared.Rent(1024);
        string authData;
        try {
            var bytesRead = await _stream.ReadAsync(authBuffer);
            authData = Utf8NoBom.GetString(authBuffer, 0, bytesRead).Trim();
        } finally {
            ArrayPool<byte>.Shared.Return(authBuffer);
        }

        try {
            var maxDecodedLength = (authData.Length * 3) / 4;
            var decodedBuffer = ArrayPool<byte>.Shared.Rent(maxDecodedLength);
            try {
                if (!Convert.TryFromBase64Chars(authData.AsSpan(), decodedBuffer, out var decodedLength)) {
                    return NoResponse(command, "Authentication failed");
                }

                var decoded = Utf8NoBom.GetString(decodedBuffer, 0, decodedLength);
                var decodedSpan = decoded.AsSpan();

                // PLAIN format: \0username\0password
                var firstNull = decodedSpan.IndexOf('\0');
                if (firstNull >= 0) {
                    var afterFirst = decodedSpan[(firstNull + 1)..];
                    var secondNull = afterFirst.IndexOf('\0');
                    if (secondNull >= 0) {
                        var username = afterFirst[..secondNull].ToString();
                        var password = afterFirst[(secondNull + 1)..].ToString();

                        var user = await userService.AuthenticateUserEntityAsync(username, password);
                        if (user != null) {
                            CurrentUser = user;
                            State = ImapConnectionState.Authenticated;
                            return OkResponse(command, "AUTHENTICATE completed");
                        }
                    }
                }
            } finally {
                ArrayPool<byte>.Shared.Return(decodedBuffer);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "Invalid authentication data");
        }

        return NoResponse(command, "Authentication failed");
    }

    private async Task<ImapResponse> HandleLogoutAsync(ImapCommand command) {
        await SendResponseAsync("* BYE LOGOUT Frimerki IMAP Server logging out");
        State = ImapConnectionState.Logout;
        return OkResponse(command, "LOGOUT completed");
    }

    private async Task<ImapResponse> HandleNoopAsync(ImapCommand command) {
        await Task.Yield();
        return OkResponse(command, "NOOP completed");
    }

    private async Task<ImapResponse> HandleSelectExamineAsync(ImapCommand command, bool readOnly) {
        if (RequiresAuth) {
            return NoResponse(command, "Must be authenticated");
        }

        if (command.Arguments.Count < 1) {
            return BadResponse(command, "SELECT/EXAMINE requires folder name");
        }

        var folderName = ImapCommandParser.UnquoteString(command.Arguments[0]);

        try {
            if (folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("DRAFTS", StringComparison.OrdinalIgnoreCase)) {
                SelectedFolder = folderName.ToUpper();
                IsReadOnly = readOnly;
                State = ImapConnectionState.Selected;

                await SendResponseAsync("* 0 EXISTS");
                await SendResponseAsync("* 0 RECENT");
                await SendResponseAsync(@"* FLAGS (\Answered \Flagged \Deleted \Seen \Draft)");
                await SendResponseAsync(@"* OK [PERMANENTFLAGS (\Answered \Flagged \Deleted \Seen \Draft \*)] Flags permitted");
                await SendResponseAsync("* OK [UIDNEXT 1] Predicted next UID");
                await SendResponseAsync("* OK [UIDVALIDITY 1] UIDs valid");

                var mode = readOnly ? "READ-ONLY" : "READ-WRITE";
                return OkResponse(command, $"[{mode}] {command.Name} completed");
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error selecting folder {Folder}", folderName);
        }

        return NoResponse(command, "Folder not found");
    }

    private async Task<ImapResponse> HandleListAsync(ImapCommand command) {
        if (RequiresAuth) {
            return NoResponse(command, "Must be authenticated");
        }

        await SendResponseAsync("* LIST () \"/\" INBOX");
        await SendResponseAsync("* LIST () \"/\" Drafts");
        return OkResponse(command, "LIST completed");
    }

    private async Task<ImapResponse> HandleAppendAsync(ImapCommand command) {
        if (RequiresAuth) {
            await Task.Yield();
            return NoResponse(command, "Must be authenticated");
        }

        await Task.Yield();
        return BadResponse(command, "APPEND requires literal data");
    }

    private async Task HandleAppendCommandWithLiteral(string commandLine, ReadOnlyMemory<byte> initialLiteral = default) {
        try {
            logger.LogInformation("APPEND: Starting processing for command: {Command}", commandLine);

            var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) {
                await SendResponseAsync($"{parts[0]} BAD APPEND requires mailbox name");
                return;
            }

            var tag = parts[0];
            var mailbox = parts[2].Trim('"');
            var lastPart = parts[^1];

            if (!lastPart.StartsWith('{') || !lastPart.EndsWith('}')) {
                logger.LogWarning("APPEND: No literal size found in command: {Command}", commandLine);
                await SendResponseAsync($"{tag} BAD Invalid APPEND syntax");
                return;
            }

            var sizeString = lastPart[1..^1];
            var isNonSync = sizeString.EndsWith('+');
            if (isNonSync) {
                sizeString = sizeString[..^1];
            }

            if (!int.TryParse(sizeString, out var literalSize)) {
                logger.LogWarning("APPEND: Invalid literal size format: {LiteralPart}", lastPart);
                await SendResponseAsync($"{tag} BAD Invalid APPEND syntax");
                return;
            }

            logger.LogInformation("APPEND: Expecting literal of size {Size} for mailbox {Mailbox}", literalSize, mailbox);
            if (!isNonSync) {
                await SendResponseAsync("+ Ready for literal data");
            }

            var buffer = ArrayPool<byte>.Shared.Rent(literalSize);
            var totalRead = 0;
            try {
                ReadOnlyMemory<byte> trailingBytes = default;
                if (!initialLiteral.IsEmpty) {
                    var toCopy = Math.Min(initialLiteral.Length, literalSize);
                    initialLiteral.Span[..toCopy].CopyTo(buffer.AsSpan(0, toCopy));
                    totalRead = toCopy;
                    if (initialLiteral.Length > toCopy) {
                        trailingBytes = initialLiteral[toCopy..];
                    }
                }

                while (totalRead < literalSize) {
                    var bytesRead = await _stream.ReadAsync(buffer.AsMemory(totalRead, literalSize - totalRead));
                    if (bytesRead == 0) {
                        break;
                    }

                    totalRead += bytesRead;
                }

                var messageContent = Utf8NoBom.GetString(buffer, 0, totalRead);
                logger.LogDebug("APPEND: Received message content of length {Length}", messageContent.Length);

                try {
                    using var toRecipients = ExtractToRecipientsFromMessage(messageContent);
                    var subject = ExtractHeaderValue(messageContent, "Subject") ?? "No Subject";

                    logger.LogInformation("APPEND: Creating message with subject '{Subject}' for mailbox '{Mailbox}'",
                        subject, mailbox);

                    var messageRequest = new MessageRequest {
                        Subject = subject,
                        Body = ExtractBodyFromMessage(messageContent),
                        ToAddress = toRecipients.Count > 0 ? toRecipients[0] : "unknown@localhost",
                        CcAddress = toRecipients.Count > 1 ? toRecipients[1] : null,
                        InReplyTo = ExtractHeaderValue(messageContent, "In-Reply-To"),
                        References = ExtractHeaderValue(messageContent, "References")
                    };

                    var folderId = mailbox.Equals("Drafts", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                    var createdMessage = await messageService.CreateMessageAsync(folderId, messageRequest);

                    var appendUid = createdMessage?.Uid > 0 ? createdMessage.Uid : createdMessage?.Id ?? 0;
                    await SendResponseAsync(appendUid > 0
                        ? $"{tag} OK [APPENDUID 1 {appendUid}] APPEND completed"
                        : $"{tag} OK APPEND completed");
                } catch (Exception ex) {
                    logger.LogError(ex, "Failed to create message via APPEND");
                    await SendResponseAsync($"{tag} NO Failed to append message");
                    return;
                }

                if (!trailingBytes.IsEmpty) {
                    await ProcessExtraCommandBytes(trailingBytes);
                }
            } finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing APPEND command");
            var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tag = parts.Length > 0 ? parts[0] : "*";
            await SendResponseAsync($"{tag} BAD APPEND processing error");
        }
    }

    private async Task<ImapResponse> HandleFetchAsync(ImapCommand command) {
        if (RequiresSelected) {
            await Task.Yield();
            return NoResponse(command, "Must have folder selected");
        }

        // Basic FETCH stub - implement full FETCH in Phase 2
        await Task.Yield();
        return OkResponse(command, "FETCH completed");
    }

    private async Task<ImapResponse> HandleSearchAsync(ImapCommand command) {
        if (RequiresSelected) {
            return NoResponse(command, "Must have folder selected");
        }

        await SendResponseAsync("* SEARCH");
        return OkResponse(command, "SEARCH completed");
    }

    private async Task<ImapResponse> HandleStoreAsync(ImapCommand command) {
        if (RequiresSelected) {
            return NoResponse(command, "Must have folder selected");
        }

        if (IsReadOnly) {
            return NoResponse(command, "Folder is read-only");
        }

        if (command.Arguments.Count < 3) {
            return BadResponse(command, "STORE requires sequence-set, data-item, and value");
        }

        try {
            var sequenceSet = command.Arguments[0];
            var dataItem = command.Arguments[1].ToUpper();
            var flagsString = string.Join(" ", command.Arguments.Skip(2));

            var flagsMatch = FlagMatchRegex().Match(flagsString);
            if (!flagsMatch.Success) {
                return BadResponse(command, "Invalid flags format");
            }

            var flagNames = flagsMatch.Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            bool isAdd = dataItem.StartsWith('+');
            bool isRemove = dataItem.StartsWith('-');
            bool isSilent = dataItem.Contains("SILENT");
            bool isReplace = !isAdd && !isRemove;

            using var messageUids = ParseSequenceSet(sequenceSet);

            foreach (var uid in messageUids) {
                var flagsRequest = new MessageFlagsRequest();

                if (isReplace) {
                    flagsRequest.Seen = false;
                    flagsRequest.Answered = false;
                    flagsRequest.Flagged = false;
                    flagsRequest.Deleted = false;
                    flagsRequest.Draft = false;
                    flagsRequest.CustomFlags = [];
                }

                ApplyFlagChanges(flagsRequest, flagNames, isRemove);

                await messageService.UpdateMessageAsync(CurrentUser!.Id, uid,
                    new MessageUpdateRequest { Flags = flagsRequest });

                if (!isSilent) {
                    var updatedMessage = await messageService.GetMessageAsync(CurrentUser.Id, uid);
                    if (updatedMessage != null) {
                        var sequenceNumber = uid; // Simplified: seq == uid for now
                        using var flagsList = GetFlagsListForResponse(updatedMessage.Flags);
                        var flags = string.Join(" ", flagsList);
                        await SendResponseAsync($"* {sequenceNumber} FETCH (FLAGS ({flags}) UID {uid})");
                    }
                }
            }

            return OkResponse(command, "STORE completed");
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing STORE command");
            return BadResponse(command, "STORE processing error");
        }
    }

    private static void ApplyFlagChanges(MessageFlagsRequest request, string[] flagNames, bool isRemove) {
        foreach (var flagName in flagNames) {
            if (StandardFlags.TryGetValue(flagName, out var entry) && entry.Set != null) {
                entry.Set(request, !isRemove);
            } else if (!flagName.StartsWith('\\')) {
                request.CustomFlags ??= [];
                if (!isRemove && !request.CustomFlags.Contains(flagName)) {
                    request.CustomFlags.Add(flagName);
                }
            }
        }
    }

    private async Task<ImapResponse> HandleExpungeAsync(ImapCommand command) {
        if (RequiresSelected) {
            return NoResponse(command, "Must have folder selected");
        }

        if (IsReadOnly) {
            return NoResponse(command, "Folder is read-only");
        }

        try {
            if (CurrentUser == null || SelectedFolder == null) {
                return BadResponse(command, "Internal error: no user or folder selected");
            }

            var deletedMessages = await GetMessagesWithDeletedFlagAsync();
            int expungedCount = 0;

            foreach (var message in deletedMessages.OrderByDescending(m => m.SequenceNumber)) {
                await SendResponseAsync($"* {message.SequenceNumber} EXPUNGE");

                if (await messageService.DeleteMessageAsync(CurrentUser.Id, message.Id)) {
                    expungedCount++;
                } else {
                    logger.LogWarning("Failed to delete message {MessageId} for user {UserId}",
                        message.Id, CurrentUser.Id);
                }
            }

            var remainingCount = await GetMessageCountInCurrentFolderAsync();
            await SendResponseAsync($"* {remainingCount} EXISTS");

            logger.LogInformation("EXPUNGE completed: {ExpungedCount} messages removed, {RemainingCount} messages remain",
                expungedCount, remainingCount);

            return OkResponse(command, "EXPUNGE completed");
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing EXPUNGE command");
            return BadResponse(command, "EXPUNGE processing error");
        }
    }

    // --- Message parsing helpers ---

    private static string ExtractHeaderValue(string messageContent, string headerName) {
        var span = messageContent.AsSpan();
        var headerSpan = headerName.AsSpan();

        foreach (var line in span.EnumerateLines()) {
            if (line.Length > headerSpan.Length + 1 &&
                line.StartsWith(headerSpan, StringComparison.OrdinalIgnoreCase) &&
                line[headerSpan.Length] == ':') {
                return line[(headerSpan.Length + 1)..].Trim().ToString();
            }
        }
        return null;
    }

    private static string ExtractBodyFromMessage(string messageContent) {
        // Find the empty line separating headers from body.
        // Handles both \r\n\r\n and \n\n line endings.
        var span = messageContent.AsSpan();
        for (int i = 0; i < span.Length - 1; i++) {
            if (span[i] != '\n') {
                continue;
            }

            if (span[i + 1] == '\n') {
                return span[(i + 2)..].Trim().ToString();
            }

            if (i + 2 < span.Length && span[i + 1] == '\r' && span[i + 2] == '\n') {
                return span[(i + 3)..].Trim().ToString();
            }
        }
        return messageContent;
    }

    private static RentedList<string> ExtractToRecipientsFromMessage(string messageContent) {
        var toHeader = ExtractHeaderValue(messageContent, "To");
        if (string.IsNullOrEmpty(toHeader)) {
            return new RentedList<string>(initialCapacity: 1);
        }

        RentedList<string> recipients = new();
        var span = toHeader.AsSpan();
        var searchFrom = 0;

        while (searchFrom < span.Length) {
            var commaIndex = span[searchFrom..].IndexOf(',');
            var email = commaIndex >= 0
                ? span.Slice(searchFrom, commaIndex).Trim()
                : span[searchFrom..].Trim();

            if (email.Length > 0) {
                recipients.Add(email.ToString());
            }

            if (commaIndex < 0) {
                break;
            }

            searchFrom += commaIndex + 1;
        }
        return recipients;
    }

    // --- Protocol I/O ---

    private async Task SendResponseAsync(string response) {
        var responseLength = Utf8NoBom.GetByteCount(response);
        var totalLength = responseLength + 2;
        var buffer = ArrayPool<byte>.Shared.Rent(totalLength);

        try {
            Utf8NoBom.GetBytes(response.AsSpan(), buffer);
            buffer[responseLength] = (byte)'\r';
            buffer[responseLength + 1] = (byte)'\n';
            await _stream.WriteAsync(buffer.AsMemory(0, totalLength));
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await _stream.FlushAsync();
        logger.LogInformation("IMAP Response: {Response}", response);
    }

    // --- Sequence set & flag helpers ---

    /// <summary>
    /// Parses an IMAP sequence set (e.g. "1", "1:5", "1,3,5:7") into a list of IDs.
    /// Currently sequence numbers and UIDs are treated identically (simplified).
    /// </summary>
    private static RentedList<int> ParseSequenceSet(string sequenceSet) {
        RentedList<int> ids = new();
        var span = sequenceSet.AsSpan();
        var searchFrom = 0;

        while (searchFrom < span.Length) {
            var commaIndex = span[searchFrom..].IndexOf(',');
            var part = commaIndex >= 0
                ? span.Slice(searchFrom, commaIndex)
                : span[searchFrom..];

            var colonIndex = part.IndexOf(':');
            if (colonIndex >= 0) {
                if (int.TryParse(part[..colonIndex], out int start) &&
                    int.TryParse(part[(colonIndex + 1)..], out int end)) {
                    for (int i = start; i <= end; i++) {
                        ids.Add(i);
                    }
                }
            } else if (int.TryParse(part, out int id)) {
                ids.Add(id);
            }

            if (commaIndex < 0) {
                break;
            }

            searchFrom += commaIndex + 1;
        }
        return ids;
    }

    private static RentedList<string> GetFlagsListForResponse(MessageFlagsResponse flags) {
        RentedList<string> result = new();
        foreach (var (name, entry) in StandardFlags) {
            if (entry.Get(flags)) {
                result.Add(name);
            }
        }
        result.AddRange(flags.CustomFlags);
        return result;
    }

    // --- Folder query helpers ---

    private async Task<List<MessageWithSequenceInfo>> GetMessagesWithDeletedFlagAsync() {
        if (CurrentUser == null || SelectedFolder == null) {
            return [];
        }

        var result = await messageService.GetMessagesAsync(CurrentUser.Id, new MessageFilterRequest {
            Folder = SelectedFolder,
            Flags = "deleted",
            Take = 1000
        });

        return result.Items.Select((msg, index) => new MessageWithSequenceInfo(
            Id: msg.Id,
            SequenceNumber: index + 1
        )).ToList();
    }

    private async Task<int?> GetMessageCountInCurrentFolderAsync() {
        if (CurrentUser == null || SelectedFolder == null) {
            return 0;
        }

        var result = await messageService.GetMessagesAsync(CurrentUser.Id, new MessageFilterRequest {
            Folder = SelectedFolder,
            Take = 1
        });
        return result.TotalCount;
    }

    private record MessageWithSequenceInfo(int Id, int SequenceNumber);

    [System.Text.RegularExpressions.GeneratedRegex(@"\(([^)]*)\)")]
    private static partial System.Text.RegularExpressions.Regex FlagMatchRegex();
}
