using System.Net.Sockets;
using System.Text;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// Handles individual IMAP client connections
/// </summary>
public class ImapSession {
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ILogger<ImapSession> _logger;
    private readonly IUserService _userService;
    private readonly IFolderService _folderService;
    private readonly IMessageService _messageService;
    private readonly ImapCommandParser _parser;

    public ImapConnectionState State { get; private set; } = ImapConnectionState.NotAuthenticated;
    public string? AuthenticatedUser { get; private set; }
    public User? CurrentUser { get; private set; }
    public string? SelectedFolder { get; private set; }
    public bool IsReadOnly { get; private set; }

    public ImapSession(
        TcpClient client,
        ILogger<ImapSession> logger,
        IUserService userService,
        IFolderService folderService,
        IMessageService messageService) {
        _client = client;
        _stream = client.GetStream();
        _logger = logger;
        _userService = userService;
        _folderService = folderService;
        _messageService = messageService;
        _parser = new ImapCommandParser();
    }

    public async Task HandleSessionAsync() {
        try {
            // Send greeting
            await SendResponseAsync("* OK [CAPABILITY IMAP4rev1 STARTTLS AUTH=PLAIN UIDPLUS] Frímerki IMAP Server ready");

            while (_client.Connected && State != ImapConnectionState.Logout) {
                var buffer = new byte[8192];
                var bytesRead = await _stream.ReadAsync(buffer);

                if (bytesRead == 0) {
                    break;
                }

                var commandLine = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                await ProcessCommandAsync(commandLine);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error in IMAP session");
        } finally {
            try {
                _client?.Close();
            } catch {
                // Ignore cleanup errors
            }
        }
    }

    private async Task ProcessCommandAsync(string commandLine) {
        try {
            _logger.LogInformation("IMAP Command: {Command}", commandLine);

            // Special handling for APPEND command with literals
            if (commandLine.Contains("APPEND") && commandLine.Contains("{")) {
                _logger.LogInformation("APPEND: Detected APPEND command with literal");
                await HandleAppendCommandWithLiteral(commandLine);
                return;
            }

            var command = _parser.ParseCommand(commandLine);
            if (command == null) {
                _logger.LogWarning("IMAP: Failed to parse command: {Command}", commandLine);
                await SendResponseAsync("* BAD Invalid command syntax");
                return;
            }

            var response = command.Name switch {
                "CAPABILITY" => await HandleCapabilityAsync(command),
                "LOGIN" => await HandleLoginAsync(command),
                "AUTHENTICATE" => await HandleAuthenticateAsync(command),
                "LOGOUT" => await HandleLogoutAsync(command),
                "NOOP" => await HandleNoopAsync(command),
                "SELECT" => await HandleSelectAsync(command),
                "EXAMINE" => await HandleExamineAsync(command),
                "LIST" => await HandleListAsync(command),
                "APPEND" => await HandleAppendAsync(command),
                "FETCH" => await HandleFetchAsync(command),
                "SEARCH" => await HandleSearchAsync(command),
                "STORE" => await HandleStoreAsync(command),
                "EXPUNGE" => await HandleExpungeAsync(command),
                _ => new ImapResponse {
                    Tag = command.Tag,
                    Type = ImapResponseType.Bad,
                    Message = "Unknown command"
                }
            };

            await SendResponseAsync($"{response.Tag} {response.Type.ToString().ToUpper()} {response.Message}");

        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing command: {Command}", commandLine);
            await SendResponseAsync("* BAD Internal server error");
        }
    }

    private async Task<ImapResponse> HandleCapabilityAsync(ImapCommand command) {
        await SendResponseAsync("* CAPABILITY IMAP4rev1 STARTTLS AUTH=PLAIN UIDPLUS");
        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Ok,
            Message = "CAPABILITY completed"
        };
    }

    private async Task<ImapResponse> HandleLoginAsync(ImapCommand command) {
        if (State != ImapConnectionState.NotAuthenticated) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "Already authenticated"
            };
        }

        if (command.Arguments.Count < 2) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "LOGIN requires username and password"
            };
        }

        var username = _parser.UnquoteString(command.Arguments[0]);
        var password = _parser.UnquoteString(command.Arguments[1]);

        try {
            var user = await _userService.AuthenticateUserEntityAsync(username, password);
            if (user != null) {
                AuthenticatedUser = username;
                CurrentUser = user;
                State = ImapConnectionState.Authenticated;

                return new ImapResponse {
                    Tag = command.Tag,
                    Type = ImapResponseType.Ok,
                    Message = "LOGIN completed"
                };
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Login failed for user {Username}", username);
        }

        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.No,
            Message = "LOGIN failed"
        };
    }

    private async Task<ImapResponse> HandleAuthenticateAsync(ImapCommand command) {
        if (State != ImapConnectionState.NotAuthenticated) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "Already authenticated"
            };
        }

        if (command.Arguments.Count < 1) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "AUTHENTICATE requires mechanism"
            };
        }

        var mechanism = command.Arguments[0].ToUpperInvariant();

        if (mechanism == "PLAIN") {
            // Send continuation response
            await SendResponseAsync("+ ");

            // Read the authentication data from the client
            var buffer = new byte[1024];
            var bytesRead = await _stream.ReadAsync(buffer);
            var authData = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            try {
                // Decode base64 authentication data
                var decodedBytes = Convert.FromBase64String(authData);
                var decoded = Encoding.UTF8.GetString(decodedBytes);

                // PLAIN format: \0username\0password
                var parts = decoded.Split('\0');
                if (parts.Length >= 3) {
                    var username = parts[1];
                    var password = parts[2];

                    // Use the user service to authenticate
                    var user = await _userService.AuthenticateUserEntityAsync(username, password);
                    if (user != null) {
                        AuthenticatedUser = username;
                        CurrentUser = user;
                        State = ImapConnectionState.Authenticated;

                        return new ImapResponse {
                            Tag = command.Tag,
                            Type = ImapResponseType.Ok,
                            Message = "AUTHENTICATE completed"
                        };
                    }
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Invalid authentication data");
            }

            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Authentication failed"
            };
        }

        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.No,
            Message = "Authentication mechanism not supported"
        };
    }

    private async Task<ImapResponse> HandleLogoutAsync(ImapCommand command) {
        await SendResponseAsync("* BYE LOGOUT Frímerki IMAP Server logging out");
        State = ImapConnectionState.Logout;

        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Ok,
            Message = "LOGOUT completed"
        };
    }

    private Task<ImapResponse> HandleNoopAsync(ImapCommand command) {
        // NOOP is a keepalive command that does nothing
        return Task.FromResult(new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Ok,
            Message = "NOOP completed"
        });
    }

    private async Task<ImapResponse> HandleSelectAsync(ImapCommand command) {
        return await HandleSelectExamineAsync(command, false);
    }

    private async Task<ImapResponse> HandleExamineAsync(ImapCommand command) {
        return await HandleSelectExamineAsync(command, true);
    }

    private async Task<ImapResponse> HandleSelectExamineAsync(ImapCommand command, bool readOnly) {
        if (State != ImapConnectionState.Authenticated && State != ImapConnectionState.Selected) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must be authenticated"
            };
        }

        if (command.Arguments.Count < 1) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "SELECT/EXAMINE requires folder name"
            };
        }

        var folderName = _parser.UnquoteString(command.Arguments[0]);

        try {
            // Handle standard folders - in full implementation, use folder service
            if (folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase) || folderName.Equals("DRAFTS", StringComparison.OrdinalIgnoreCase)) {
                SelectedFolder = folderName.ToUpper();
                IsReadOnly = readOnly;
                State = ImapConnectionState.Selected;

                // Send required SELECT responses
                await SendResponseAsync("* 0 EXISTS");      // Message count
                await SendResponseAsync("* 0 RECENT");      // Recent message count
                await SendResponseAsync("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)");
                await SendResponseAsync("* OK [PERMANENTFLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft \\*)] Flags permitted");
                await SendResponseAsync("* OK [UIDNEXT 1] Predicted next UID");
                await SendResponseAsync("* OK [UIDVALIDITY 1] UIDs valid");

                // The access mode should be in the final OK response, not in an untagged response
                var readOnlyMsg = readOnly ? "READ-ONLY" : "READ-WRITE";

                return new ImapResponse {
                    Tag = command.Tag,
                    Type = ImapResponseType.Ok,
                    Message = $"[{readOnlyMsg}] {command.Name} completed"
                };
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error selecting folder {Folder}", folderName);
        }

        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.No,
            Message = "Folder not found"
        };
    }

    private async Task<ImapResponse> HandleListAsync(ImapCommand command) {
        if (State != ImapConnectionState.Authenticated && State != ImapConnectionState.Selected) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must be authenticated"
            };
        }

        // Basic LIST implementation - return standard folders
        await SendResponseAsync("* LIST () \"/\" INBOX");
        await SendResponseAsync("* LIST () \"/\" Drafts");

        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Ok,
            Message = "LIST completed"
        };
    }

    private Task<ImapResponse> HandleAppendAsync(ImapCommand command) {
        if (State != ImapConnectionState.Authenticated && State != ImapConnectionState.Selected) {
            return Task.FromResult(new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must be authenticated"
            });
        }

        // This handles the basic APPEND case, literal handling is done in HandleAppendCommandWithLiteral
        return Task.FromResult(new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Bad,
            Message = "APPEND requires literal data"
        });
    }

    private async Task HandleAppendCommandWithLiteral(string commandLine) {
        try {
            _logger.LogInformation("APPEND: Starting processing for command: {Command}", commandLine);

            // Parse the command manually for APPEND with literal
            var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) { // At minimum: tag APPEND mailbox
                await SendResponseAsync($"{parts[0]} BAD APPEND requires mailbox name");
                return;
            }

            var tag = parts[0];
            var commandName = parts[1];
            var mailbox = parts[2].Trim('"');

            // Find the literal size at the end
            var lastPart = parts[^1];
            if (lastPart.StartsWith('{') && lastPart.EndsWith('}')) {
                var sizeString = lastPart[1..^1];
                if (int.TryParse(sizeString, out var literalSize)) {
                    _logger.LogInformation("APPEND: Expecting literal of size {Size} for mailbox {Mailbox}", literalSize, mailbox);

                    // Send continuation response
                    await SendResponseAsync("+ Ready for literal data");

                    // Read the literal data
                    var buffer = new byte[literalSize];
                    var totalRead = 0;
                    while (totalRead < literalSize) {
                        var bytesRead = await _stream.ReadAsync(buffer.AsMemory(totalRead, literalSize - totalRead));
                        if (bytesRead == 0) {
                            break;
                        }
                        totalRead += bytesRead;
                    }

                    var messageContent = Encoding.UTF8.GetString(buffer, 0, totalRead);
                    _logger.LogDebug("APPEND: Received message content of length {Length}", messageContent.Length);

                    try {
                        // Create new message using message service
                        var toRecipients = ExtractToRecipientsFromMessage(messageContent);
                        var toAddress = toRecipients.FirstOrDefault("unknown@localhost");
                        var subject = ExtractHeaderValue(messageContent, "Subject") ?? "No Subject";

                        _logger.LogInformation("APPEND: Creating message with subject '{Subject}' for mailbox '{Mailbox}'",
                            subject, mailbox);

                        var messageRequest = new MessageRequest {
                            Subject = subject,
                            Body = ExtractBodyFromMessage(messageContent),
                            ToAddress = toAddress,
                            CcAddress = toRecipients.Skip(1).FirstOrDefault(),
                            InReplyTo = ExtractHeaderValue(messageContent, "In-Reply-To"),
                            References = ExtractHeaderValue(messageContent, "References")
                        };

                        // Use folder ID 1 for INBOX, 2 for Drafts
                        var folderId = mailbox.Equals("Drafts", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                        var createdMessage = await _messageService.CreateMessageAsync(folderId, messageRequest);

                        // Parse flags if present (look for parentheses in command parts)
                        List<string> flags = [];
                        for (int i = 3; i < parts.Length - 1; i++) {
                            if (parts[i].StartsWith('(') || flags.Any()) {
                                var flag = parts[i].Trim('(', ')');
                                if (!string.IsNullOrEmpty(flag)) {
                                    flags.Add(flag);
                                }
                                if (parts[i].EndsWith(')')) {
                                    break;
                                }
                            }
                        }

                        // If we have flags, update them after creation
                        if (flags.Any()) {
                            var flagsRequest = new MessageFlagsRequest {
                                Seen = flags.Contains("\\Seen"),
                                Answered = flags.Contains("\\Answered"),
                                Flagged = flags.Contains("\\Flagged"),
                                Deleted = flags.Contains("\\Deleted"),
                                Draft = flags.Contains("\\Draft") || mailbox.Equals("Drafts", StringComparison.OrdinalIgnoreCase)
                            };

                            var updateRequest = new MessageUpdateRequest {
                                Flags = flagsRequest
                            };

                            await _messageService.UpdateMessageAsync(folderId, createdMessage.Id, updateRequest);
                        }

                        await SendResponseAsync($"{tag} OK [APPENDUID 1 {createdMessage.Id}] APPEND completed");
                        _logger.LogInformation("APPEND: Sent response with APPENDUID 1 {MessageId}", createdMessage.Id);
                        return;

                    } catch (Exception ex) {
                        _logger.LogError(ex, "Failed to create message via APPEND");
                        await SendResponseAsync($"{tag} NO Failed to append message");
                        return;
                    }
                } else {
                    _logger.LogWarning("APPEND: Invalid literal size format: {LiteralPart}", lastPart);
                }
            } else {
                _logger.LogWarning("APPEND: No literal size found in command: {Command}", commandLine);
            }

            await SendResponseAsync($"{tag} BAD Invalid APPEND syntax");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing APPEND command");
            var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tag = parts.Length > 0 ? parts[0] : "*";
            await SendResponseAsync($"{tag} BAD APPEND processing error");
        }
    }

    private Task<ImapResponse> HandleFetchAsync(ImapCommand command) {
        if (State != ImapConnectionState.Selected) {
            return Task.FromResult(new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must have folder selected"
            });
        }

        // Basic FETCH stub - implement full FETCH in Phase 2
        return Task.FromResult(new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Ok,
            Message = "FETCH completed"
        });
    }

    private async Task<ImapResponse> HandleSearchAsync(ImapCommand command) {
        if (State != ImapConnectionState.Selected) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must have folder selected"
            };
        }

        // Basic SEARCH stub - return empty results for now
        await SendResponseAsync("* SEARCH");

        return new ImapResponse {
            Tag = command.Tag,
            Type = ImapResponseType.Ok,
            Message = "SEARCH completed"
        };
    }

    private async Task<ImapResponse> HandleStoreAsync(ImapCommand command) {
        if (State != ImapConnectionState.Selected) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must have folder selected"
            };
        }

        if (IsReadOnly) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Folder is read-only"
            };
        }

        try {
            // Parse STORE command: STORE sequence-set data-item value
            // Examples:
            // STORE 1 +FLAGS (\Seen)
            // STORE 2:4 FLAGS (\Answered \Flagged)
            // STORE 1 -FLAGS.SILENT (\Deleted)

            if (command.Arguments.Count < 3) {
                return new ImapResponse {
                    Tag = command.Tag,
                    Type = ImapResponseType.Bad,
                    Message = "STORE requires sequence-set, data-item, and value"
                };
            }

            var sequenceSet = command.Arguments[0];
            var dataItem = command.Arguments[1].ToUpper();
            var flagsString = string.Join(" ", command.Arguments.Skip(2));

            // Parse flags from parentheses
            var flagsMatch = System.Text.RegularExpressions.Regex.Match(flagsString, @"\(([^)]*)\)");
            if (!flagsMatch.Success) {
                return new ImapResponse {
                    Tag = command.Tag,
                    Type = ImapResponseType.Bad,
                    Message = "Invalid flags format"
                };
            }

            var flagNames = flagsMatch.Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            // Determine operation type
            bool isAdd = dataItem.StartsWith("+");
            bool isRemove = dataItem.StartsWith("-");
            bool isSilent = dataItem.Contains("SILENT");
            bool isReplace = !isAdd && !isRemove;

            // Parse sequence set and get message UIDs
            var messageUids = await ParseSequenceSetToUidsAsync(sequenceSet, command.Name == "UID");

            foreach (var uid in messageUids) {
                // Create flag update request
                var flagsRequest = new MessageFlagsRequest();

                if (isReplace) {
                    // Replace all flags - set all standard flags to false first, then set specified ones
                    flagsRequest.Seen = false;
                    flagsRequest.Answered = false;
                    flagsRequest.Flagged = false;
                    flagsRequest.Deleted = false;
                    flagsRequest.Draft = false;
                    flagsRequest.CustomFlags = [];
                }

                // Apply flag changes
                foreach (var flagName in flagNames) {
                    switch (flagName.ToUpper()) {
                        case "\\SEEN":
                            if (isRemove) {
                                flagsRequest.Seen = false;
                            } else if (isAdd || isReplace) {
                                flagsRequest.Seen = true;
                            }
                            break;
                        case "\\ANSWERED":
                            if (isRemove) {
                                flagsRequest.Answered = false;
                            } else if (isAdd || isReplace) {
                                flagsRequest.Answered = true;
                            }
                            break;
                        case "\\FLAGGED":
                            if (isRemove) {
                                flagsRequest.Flagged = false;
                            } else if (isAdd || isReplace) {
                                flagsRequest.Flagged = true;
                            }
                            break;
                        case "\\DELETED":
                            if (isRemove) {
                                flagsRequest.Deleted = false;
                            } else if (isAdd || isReplace) {
                                flagsRequest.Deleted = true;
                            }
                            break;
                        case "\\DRAFT":
                            if (isRemove) {
                                flagsRequest.Draft = false;
                            } else if (isAdd || isReplace) {
                                flagsRequest.Draft = true;
                            }
                            break;
                        default:
                            // Handle custom flags
                            if (!flagName.StartsWith("\\")) {
                                flagsRequest.CustomFlags ??= [];
                                if (isAdd || isReplace) {
                                    if (!flagsRequest.CustomFlags.Contains(flagName)) {
                                        flagsRequest.CustomFlags.Add(flagName);
                                    }
                                }
                                // For remove, we'll handle this in the service
                            }
                            break;
                    }
                }

                // Update message flags
                var updateRequest = new MessageUpdateRequest {
                    Flags = flagsRequest
                };

                await _messageService.UpdateMessageAsync(CurrentUser!.Id, uid, updateRequest);

                // Send untagged FETCH response if not silent
                if (!isSilent) {
                    // Get updated message to send FETCH response
                    var updatedMessage = await _messageService.GetMessageAsync(CurrentUser.Id, uid);
                    if (updatedMessage != null) {
                        var sequenceNumber = await GetSequenceNumberByUidAsync(uid);
                        var flags = string.Join(" ", GetFlagsListForResponse(updatedMessage.Flags));
                        await SendResponseAsync($"* {sequenceNumber} FETCH (FLAGS ({flags}) UID {uid})");
                    }
                }
            }

            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Ok,
                Message = "STORE completed"
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing STORE command");
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "STORE processing error"
            };
        }
    }

    private async Task<ImapResponse> HandleExpungeAsync(ImapCommand command) {
        if (State != ImapConnectionState.Selected) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Must have folder selected"
            };
        }

        if (IsReadOnly) {
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.No,
                Message = "Folder is read-only"
            };
        }

        try {
            if (CurrentUser == null || SelectedFolder == null) {
                return new ImapResponse {
                    Tag = command.Tag,
                    Type = ImapResponseType.Bad,
                    Message = "Internal error: no user or folder selected"
                };
            }

            // Get messages with \Deleted flag in current folder
            var deletedMessages = await GetMessagesWithDeletedFlagAsync();
            int expungedCount = 0;

            // Process messages in reverse sequence order to maintain correct sequence numbers
            // when sending EXPUNGE responses (RFC 3501 requirement)
            foreach (var message in deletedMessages.OrderByDescending(m => m.SequenceNumber)) {
                // Send untagged EXPUNGE response before deleting
                await SendResponseAsync($"* {message.SequenceNumber} EXPUNGE");

                // Permanently delete the message
                var deleted = await _messageService.DeleteMessageAsync(CurrentUser.Id, message.Id);
                if (deleted) {
                    expungedCount++;
                } else {
                    _logger.LogWarning("Failed to delete message {MessageId} for user {UserId}",
                        message.Id, CurrentUser.Id);
                }
            }

            // Update EXISTS count after expunging
            var remainingCount = await GetMessageCountInCurrentFolderAsync();
            await SendResponseAsync($"* {remainingCount} EXISTS");

            _logger.LogInformation("EXPUNGE completed: {ExpungedCount} messages removed, {RemainingCount} messages remain",
                expungedCount, remainingCount);

            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Ok,
                Message = $"EXPUNGE completed"
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing EXPUNGE command");
            return new ImapResponse {
                Tag = command.Tag,
                Type = ImapResponseType.Bad,
                Message = "EXPUNGE processing error"
            };
        }
    }

    private string? ExtractHeaderValue(string messageContent, string headerName) {
        var headerPrefix = $"{headerName}:";
        var span = messageContent.AsSpan();

        foreach (var line in span.EnumerateLines()) {
            if (line.StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase)) {
                return line[headerPrefix.Length..].Trim().ToString();
            }
        }
        return null;
    }

    private string ExtractBodyFromMessage(string messageContent) {
        // Find the empty line that separates headers from body
        var emptyLineIndex = messageContent.IndexOf("\n\n");
        if (emptyLineIndex == -1) {
            emptyLineIndex = messageContent.IndexOf("\r\n\r\n");
        }

        if (emptyLineIndex >= 0) {
            return messageContent[(emptyLineIndex + 2)..].Trim();
        }

        return messageContent; // If no headers separator found, treat all as body
    }

    private IReadOnlyList<string> ExtractToRecipientsFromMessage(string messageContent) {
        var toHeader = ExtractHeaderValue(messageContent, "To");
        if (string.IsNullOrEmpty(toHeader)) {
            return [];
        }

        return toHeader.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(email => email.Trim())
                      .ToList();
    }

    private async Task SendResponseAsync(string response) {
        var bytes = Encoding.UTF8.GetBytes(response + "\r\n");
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
        _logger.LogInformation("IMAP Response: {Response}", response);
    }

    private Task<List<int>> ParseSequenceSetToUidsAsync(string sequenceSet, bool isUid) {
        List<int> uids = [];

        if (isUid) {
            // For UID commands, the sequence set contains UIDs directly
            var parts = sequenceSet.Split(',');
            foreach (var part in parts) {
                if (part.Contains(':')) {
                    var range = part.Split(':');
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end)) {
                        for (int uid = start; uid <= end; uid++) {
                            uids.Add(uid);
                        }
                    }
                } else if (int.TryParse(part, out int uid)) {
                    uids.Add(uid);
                }
            }
        } else {
            // For sequence number commands, we need to convert sequence numbers to UIDs
            // This is a simplified implementation - in a full implementation, you'd query the database
            // to get the actual UIDs for the given sequence numbers in the current folder
            var parts = sequenceSet.Split(',');
            foreach (var part in parts) {
                if (part.Contains(':')) {
                    var range = part.Split(':');
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end)) {
                        for (int seq = start; seq <= end; seq++) {
                            // For now, assume sequence number equals UID (simplified)
                            uids.Add(seq);
                        }
                    }
                } else if (int.TryParse(part, out int seq)) {
                    // For now, assume sequence number equals UID (simplified)
                    uids.Add(seq);
                }
            }
        }

        return Task.FromResult(uids);
    }

    private Task<int> GetSequenceNumberByUidAsync(int uid) {
        // In a full implementation, this would query the database to get the actual sequence number
        // for the given UID in the current folder. For now, return UID as sequence number (simplified)
        return Task.FromResult(uid);
    }

    private List<string> GetFlagsListForResponse(MessageFlagsResponse flags) {
        List<string> flagList = [];

        if (flags.Seen) {
            flagList.Add("\\Seen");
        }
        if (flags.Answered) {
            flagList.Add("\\Answered");
        }
        if (flags.Flagged) {
            flagList.Add("\\Flagged");
        }
        if (flags.Deleted) {
            flagList.Add("\\Deleted");
        }
        if (flags.Draft) {
            flagList.Add("\\Draft");
        }
        if (flags.Recent) {
            flagList.Add("\\Recent");
        }

        // Add custom flags
        flagList.AddRange(flags.CustomFlags);

        return flagList;
    }

    /// <summary>
    /// Gets all messages with the \Deleted flag in the current folder
    /// </summary>
    private async Task<List<MessageWithSequenceInfo>> GetMessagesWithDeletedFlagAsync() {
        if (CurrentUser == null || SelectedFolder == null) {
            return [];
        }

        var request = new MessageFilterRequest {
            Folder = SelectedFolder,
            Flags = "deleted",
            Take = 1000  // Get all deleted messages (reasonable limit)
        };

        var result = await _messageService.GetMessagesAsync(CurrentUser.Id, request);

        // Map to our internal structure with sequence numbers
        // For now, we'll use a simplified approach where we generate sequence numbers
        // In a full implementation, these would come from the database
        return result.Items.Select((msg, index) => new MessageWithSequenceInfo {
            Id = msg.Id,
            Uid = 0,  // Simplified - UID not needed for deletion, only message ID
            SequenceNumber = index + 1  // Simplified - should be actual sequence number from folder
        }).ToList();
    }

    /// <summary>
    /// Gets the count of messages in the current folder (excluding deleted messages)
    /// </summary>
    private async Task<int> GetMessageCountInCurrentFolderAsync() {
        if (CurrentUser == null || SelectedFolder == null) {
            return 0;
        }

        var request = new MessageFilterRequest {
            Folder = SelectedFolder,
            Take = 1  // We only need the count, not the actual messages
        };

        var result = await _messageService.GetMessagesAsync(CurrentUser.Id, request);
        return result.TotalCount ?? 0;
    }

    /// <summary>
    /// Internal structure for tracking message sequence information
    /// </summary>
    private class MessageWithSequenceInfo {
        public int Id { get; set; }
        public int Uid { get; set; }
        public int SequenceNumber { get; set; }
    }
}
