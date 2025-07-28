using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;

namespace Frimerki.Services.Message;

public class MessageService : IMessageService {
    private readonly EmailDbContext _context;
    private readonly ILogger<MessageService> _logger;

    public MessageService(EmailDbContext context, ILogger<MessageService> logger) {
        _context = context;
        _logger = logger;
    }

    public async Task<MessageListResponse> GetMessagesAsync(int userId, MessageFilterRequest request) {
        _logger.LogInformation("Getting messages for user {UserId} with filters", userId);

        // Validate take parameter
        var take = Math.Min(request.Take, 100);

        // Build base query
        var query = _context.UserMessages
            .Where(um => um.UserId == userId)
            .Include(um => um.Message)
            .Include(um => um.Folder)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(request.Folder)) {
            query = query.Where(um => um.Folder.Name == request.Folder);
        }

        if (request.FolderId.HasValue) {
            query = query.Where(um => um.FolderId == request.FolderId.Value);
        }

        if (!string.IsNullOrEmpty(request.From)) {
            query = query.Where(um => um.Message.FromAddress.Contains(request.From));
        }

        if (!string.IsNullOrEmpty(request.To)) {
            query = query.Where(um => um.Message.ToAddress != null && um.Message.ToAddress.Contains(request.To));
        }

        if (request.Since.HasValue) {
            query = query.Where(um => um.Message.SentDate >= request.Since.Value);
        }

        if (request.Before.HasValue) {
            query = query.Where(um => um.Message.SentDate <= request.Before.Value);
        }

        if (request.MinSize.HasValue) {
            query = query.Where(um => um.Message.MessageSize >= request.MinSize.Value);
        }

        if (request.MaxSize.HasValue) {
            query = query.Where(um => um.Message.MessageSize <= request.MaxSize.Value);
        }

        // Apply flag filtering
        if (!string.IsNullOrEmpty(request.Flags)) {
            query = ApplyFlagFiltering(query, userId, request.Flags);
        }

        // Apply full-text search
        if (!string.IsNullOrEmpty(request.Q)) {
            var searchTerm = request.Q.ToLower();
            query = query.Where(um =>
                (um.Message.Subject != null && um.Message.Subject.ToLower().Contains(searchTerm)) ||
                (um.Message.Body != null && um.Message.Body.ToLower().Contains(searchTerm)) ||
                um.Message.FromAddress.ToLower().Contains(searchTerm));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = ApplySorting(query, request.SortBy, request.SortOrder);

        // Apply pagination
        var messages = await query
            .Skip(request.Skip)
            .Take(take)
            .Select(um => new MessageListItemResponse {
                Id = um.Message.Id,
                Subject = um.Message.Subject ?? string.Empty,
                FromAddress = um.Message.FromAddress,
                ToAddress = um.Message.ToAddress ?? string.Empty,
                SentDate = um.Message.SentDate ?? um.Message.ReceivedAt,
                Folder = um.Folder.Name,
                HasAttachments = um.Message.Attachments.Any(),
                MessageSize = um.Message.MessageSize,
                MessageSizeFormatted = FormatMessageSize(um.Message.MessageSize),
                Flags = GetMessageFlags(um.Message.MessageFlags.Where(mf => mf.UserId == userId))
            })
            .ToListAsync();

        // Generate next URL if there are more items
        string? nextUrl = null;
        if (request.Skip + take < totalCount) {
            nextUrl = BuildNextUrl(request, request.Skip + take);
        }

        var response = new MessageListResponse {
            Messages = messages,
            Pagination = new MessagePaginationResponse {
                Skip = request.Skip,
                Take = take,
                TotalCount = totalCount,
                NextUrl = nextUrl
            },
            AppliedFilters = BuildAppliedFilters(request)
        };

        _logger.LogInformation("Retrieved {Count} messages for user {UserId}", messages.Count, userId);
        return response;
    }

    public async Task<MessageResponse?> GetMessageAsync(int userId, int messageId) {
        _logger.LogInformation("Getting message {MessageId} for user {UserId}", messageId, userId);

        var userMessage = await _context.UserMessages
            .Where(um => um.UserId == userId && um.Message.Id == messageId)
            .Include(um => um.Message)
                .ThenInclude(m => m.Attachments)
            .Include(um => um.Message.MessageFlags.Where(mf => mf.UserId == userId))
            .Include(um => um.Folder)
            .FirstOrDefaultAsync();

        if (userMessage == null) {
            _logger.LogWarning("Message {MessageId} not found for user {UserId}", messageId, userId);
            return null;
        }

        var message = userMessage.Message;
        var envelope = ParseEnvelope(message.Envelope);
        var bodyStructure = ParseBodyStructure(message.BodyStructure);
        var flags = GetMessageFlags(message.MessageFlags.Where(mf => mf.UserId == userId));

        var response = new MessageResponse {
            Id = message.Id,
            Subject = message.Subject ?? string.Empty,
            FromAddress = message.FromAddress,
            ToAddress = message.ToAddress ?? string.Empty,
            CcAddress = message.CcAddress,
            BccAddress = message.BccAddress,
            SentDate = message.SentDate ?? message.ReceivedAt,
            ReceivedAt = message.ReceivedAt,
            MessageSize = message.MessageSize,
            Body = message.Body ?? string.Empty,
            BodyHtml = message.BodyHtml,
            Headers = message.Headers,
            Envelope = envelope,
            BodyStructure = bodyStructure,
            Flags = flags,
            Attachments = message.Attachments.Select(a => new MessageAttachmentResponse {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType ?? "application/octet-stream",
                Size = a.Size ?? 0,
                SizeFormatted = FormatMessageSize(a.Size ?? 0),
                Path = a.FilePath ?? string.Empty
            }).ToList(),
            Uid = userMessage.Uid,
            UidValidity = message.UidValidity,
            InternalDate = userMessage.ReceivedAt,
            InReplyTo = message.InReplyTo,
            References = message.References,
            Folder = userMessage.Folder.Name
        };

        _logger.LogInformation("Retrieved message {MessageId} for user {UserId}", messageId, userId);
        return response;
    }

    public async Task<MessageResponse> CreateMessageAsync(int userId, MessageRequest request) {
        _logger.LogInformation("Creating message for user {UserId}", userId);

        // Get user's SENT folder
        var sentFolder = await _context.Folders
            .Where(f => f.UserId == userId && f.SystemFolderType == "SENT")
            .FirstOrDefaultAsync();

        if (sentFolder == null) {
            throw new InvalidOperationException("User's SENT folder not found");
        }

        // Generate unique UIDs
        var uid = await GetNextUidAsync();
        var messageId = $"<{Guid.NewGuid()}@{DateTime.UtcNow:yyyyMMddHHmmss}>";

        // Create the message
        var message = new Frimerki.Models.Entities.Message {
            HeaderMessageId = messageId,
            FromAddress = $"{await GetUserEmailAsync(userId)}",
            ToAddress = request.ToAddress,
            CcAddress = request.CcAddress,
            BccAddress = request.BccAddress,
            Subject = request.Subject,
            Body = request.Body,
            BodyHtml = request.BodyHtml,
            Headers = BuildHeaders(messageId, request),
            MessageSize = CalculateMessageSize(request),
            SentDate = DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow,
            InReplyTo = request.InReplyTo,
            References = request.References,
            Uid = uid,
            UidValidity = 1,
            Envelope = BuildEnvelopeJson(request, await GetUserEmailAsync(userId)),
            BodyStructure = BuildBodyStructureJson(request)
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        // Create UserMessage relationship
        var userMessage = new UserMessage {
            UserId = userId,
            MessageId = message.Id,
            FolderId = sentFolder.Id,
            Uid = uid,
            ReceivedAt = DateTime.UtcNow
        };

        _context.UserMessages.Add(userMessage);

        // Set default flags (mark as seen since user is sending it)
        var seenFlag = new MessageFlag {
            MessageId = message.Id,
            UserId = userId,
            FlagName = "\\Seen",
            IsSet = true
        };

        _context.MessageFlags.Add(seenFlag);

        // Update folder statistics
        sentFolder.Exists += 1;
        sentFolder.UidNext = uid + 1;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Created message {MessageId} for user {UserId}", message.Id, userId);

        // Return the created message
        return await GetMessageAsync(userId, message.Id) ?? throw new InvalidOperationException("Failed to retrieve created message");
    }

    public async Task<MessageResponse?> UpdateMessageAsync(int userId, int messageId, MessageUpdateRequest request) {
        _logger.LogInformation("Updating message {MessageId} for user {UserId}", messageId, userId);

        var userMessage = await _context.UserMessages
            .Where(um => um.UserId == userId && um.Message.Id == messageId)
            .Include(um => um.Message)
            .Include(um => um.Folder)
            .FirstOrDefaultAsync();

        if (userMessage == null) {
            _logger.LogWarning("Message {MessageId} not found for user {UserId}", messageId, userId);
            return null;
        }

        var message = userMessage.Message;
        bool hasChanges = false;

        // Update flags if provided
        if (request.Flags != null) {
            await UpdateMessageFlagsAsync(userId, messageId, request.Flags);
            hasChanges = true;
        }

        // Move to different folder if provided
        if (request.FolderId.HasValue && request.FolderId.Value != userMessage.FolderId) {
            var targetFolder = await _context.Folders
                .Where(f => f.UserId == userId && f.Id == request.FolderId.Value)
                .FirstOrDefaultAsync();

            if (targetFolder == null) {
                throw new ArgumentException($"Folder {request.FolderId.Value} not found");
            }

            // Update folder statistics
            userMessage.Folder.Exists -= 1;
            targetFolder.Exists += 1;

            userMessage.FolderId = request.FolderId.Value;
            userMessage.Uid = targetFolder.UidNext;
            targetFolder.UidNext += 1;

            hasChanges = true;
        }

        // Update message content if it's a draft
        if (IsDraftMessage(userId, messageId)) {
            if (!string.IsNullOrEmpty(request.Subject)) {
                message.Subject = request.Subject;
                hasChanges = true;
            }

            if (!string.IsNullOrEmpty(request.Body)) {
                message.Body = request.Body;
                hasChanges = true;
            }

            if (!string.IsNullOrEmpty(request.BodyHtml)) {
                message.BodyHtml = request.BodyHtml;
                hasChanges = true;
            }

            if (hasChanges) {
                message.MessageSize = CalculateMessageSize(message);
            }
        }

        if (hasChanges) {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated message {MessageId} for user {UserId}", messageId, userId);
        }

        return await GetMessageAsync(userId, messageId);
    }

    public async Task<bool> DeleteMessageAsync(int userId, int messageId) {
        _logger.LogInformation("Deleting message {MessageId} for user {UserId}", messageId, userId);

        var userMessage = await _context.UserMessages
            .Where(um => um.UserId == userId && um.Message.Id == messageId)
            .Include(um => um.Folder)
            .FirstOrDefaultAsync();

        if (userMessage == null) {
            _logger.LogWarning("Message {MessageId} not found for user {UserId}", messageId, userId);
            return false;
        }

        // Get user's TRASH folder
        var trashFolder = await _context.Folders
            .Where(f => f.UserId == userId && f.SystemFolderType == "TRASH")
            .FirstOrDefaultAsync();

        if (trashFolder == null) {
            _logger.LogError("User {UserId} has no TRASH folder", userId);
            return false;
        }

        // Move to trash instead of permanent deletion
        userMessage.Folder.Exists -= 1;
        trashFolder.Exists += 1;

        userMessage.FolderId = trashFolder.Id;
        userMessage.Uid = trashFolder.UidNext;
        trashFolder.UidNext += 1;

        // Mark as deleted
        var deletedFlag = await _context.MessageFlags
            .Where(mf => mf.MessageId == messageId && mf.UserId == userId && mf.FlagName == "\\Deleted")
            .FirstOrDefaultAsync();

        if (deletedFlag == null) {
            _context.MessageFlags.Add(new MessageFlag {
                MessageId = messageId,
                UserId = userId,
                FlagName = "\\Deleted",
                IsSet = true
            });
        } else {
            deletedFlag.IsSet = true;
            deletedFlag.ModifiedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Moved message {MessageId} to trash for user {UserId}", messageId, userId);
        return true;
    }

    // Helper methods
    private IQueryable<UserMessage> ApplyFlagFiltering(IQueryable<UserMessage> query, int userId, string flags) {
        return flags.ToLower() switch {
            "read" or "seen" => query.Where(um => um.Message.MessageFlags
                .Any(mf => mf.UserId == userId && mf.FlagName == "\\Seen" && mf.IsSet)),
            "unread" or "unseen" => query.Where(um => !um.Message.MessageFlags
                .Any(mf => mf.UserId == userId && mf.FlagName == "\\Seen" && mf.IsSet)),
            "flagged" => query.Where(um => um.Message.MessageFlags
                .Any(mf => mf.UserId == userId && mf.FlagName == "\\Flagged" && mf.IsSet)),
            "answered" => query.Where(um => um.Message.MessageFlags
                .Any(mf => mf.UserId == userId && mf.FlagName == "\\Answered" && mf.IsSet)),
            "draft" => query.Where(um => um.Message.MessageFlags
                .Any(mf => mf.UserId == userId && mf.FlagName == "\\Draft" && mf.IsSet)),
            "deleted" => query.Where(um => um.Message.MessageFlags
                .Any(mf => mf.UserId == userId && mf.FlagName == "\\Deleted" && mf.IsSet)),
            _ => query
        };
    }

    private IQueryable<UserMessage> ApplySorting(IQueryable<UserMessage> query, string sortBy, string sortOrder) {
        var isDescending = sortOrder.ToLower() == "desc";

        return sortBy.ToLower() switch {
            "subject" => isDescending
                ? query.OrderByDescending(um => um.Message.Subject)
                : query.OrderBy(um => um.Message.Subject),
            "sender" or "from" => isDescending
                ? query.OrderByDescending(um => um.Message.FromAddress)
                : query.OrderBy(um => um.Message.FromAddress),
            "size" => isDescending
                ? query.OrderByDescending(um => um.Message.MessageSize)
                : query.OrderBy(um => um.Message.MessageSize),
            _ => isDescending // default to date
                ? query.OrderByDescending(um => um.Message.SentDate ?? um.Message.ReceivedAt)
                : query.OrderBy(um => um.Message.SentDate ?? um.Message.ReceivedAt)
        };
    }

    private string BuildNextUrl(MessageFilterRequest request, int nextSkip) {
        var queryParams = new List<string> { $"skip={nextSkip}", $"take={request.Take}" };

        if (!string.IsNullOrEmpty(request.Q)) queryParams.Add($"q={Uri.EscapeDataString(request.Q)}");
        if (!string.IsNullOrEmpty(request.Folder)) queryParams.Add($"folder={Uri.EscapeDataString(request.Folder)}");
        if (request.FolderId.HasValue) queryParams.Add($"folderId={request.FolderId.Value}");
        if (!string.IsNullOrEmpty(request.Flags)) queryParams.Add($"flags={Uri.EscapeDataString(request.Flags)}");
        if (!string.IsNullOrEmpty(request.From)) queryParams.Add($"from={Uri.EscapeDataString(request.From)}");
        if (!string.IsNullOrEmpty(request.To)) queryParams.Add($"to={Uri.EscapeDataString(request.To)}");
        if (request.Since.HasValue) queryParams.Add($"since={request.Since.Value:yyyy-MM-dd}");
        if (request.Before.HasValue) queryParams.Add($"before={request.Before.Value:yyyy-MM-dd}");
        if (request.MinSize.HasValue) queryParams.Add($"minSize={request.MinSize.Value}");
        if (request.MaxSize.HasValue) queryParams.Add($"maxSize={request.MaxSize.Value}");
        if (request.SortBy != "date") queryParams.Add($"sortBy={Uri.EscapeDataString(request.SortBy)}");
        if (request.SortOrder != "desc") queryParams.Add($"sortOrder={Uri.EscapeDataString(request.SortOrder)}");

        return $"/api/messages?{string.Join("&", queryParams)}";
    }

    private Dictionary<string, object> BuildAppliedFilters(MessageFilterRequest request) {
        var filters = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(request.Q)) filters["q"] = request.Q;
        if (!string.IsNullOrEmpty(request.Folder)) filters["folder"] = request.Folder;
        if (request.FolderId.HasValue) filters["folderId"] = request.FolderId.Value;
        if (!string.IsNullOrEmpty(request.Flags)) filters["flags"] = request.Flags;
        if (!string.IsNullOrEmpty(request.From)) filters["from"] = request.From;
        if (!string.IsNullOrEmpty(request.To)) filters["to"] = request.To;
        if (request.Since.HasValue) filters["since"] = request.Since.Value.ToString("yyyy-MM-dd");
        if (request.Before.HasValue) filters["before"] = request.Before.Value.ToString("yyyy-MM-dd");
        if (request.MinSize.HasValue) filters["minSize"] = request.MinSize.Value;
        if (request.MaxSize.HasValue) filters["maxSize"] = request.MaxSize.Value;

        return filters;
    }

    private MessageFlagsResponse GetMessageFlags(IEnumerable<MessageFlag> flags) {
        var flagDict = flags.Where(f => f.IsSet).ToDictionary(f => f.FlagName, f => f.IsSet);

        return new MessageFlagsResponse {
            Seen = flagDict.GetValueOrDefault("\\Seen", false),
            Answered = flagDict.GetValueOrDefault("\\Answered", false),
            Flagged = flagDict.GetValueOrDefault("\\Flagged", false),
            Deleted = flagDict.GetValueOrDefault("\\Deleted", false),
            Draft = flagDict.GetValueOrDefault("\\Draft", false),
            Recent = flagDict.GetValueOrDefault("\\Recent", false),
            CustomFlags = flags.Where(f => f.IsSet && !f.FlagName.StartsWith("\\"))
                .Select(f => f.FlagName).ToList()
        };
    }

    private async Task UpdateMessageFlagsAsync(int userId, int messageId, MessageFlagsRequest flagsRequest) {
        var standardFlags = new[] {
            ("\\Seen", flagsRequest.Seen),
            ("\\Answered", flagsRequest.Answered),
            ("\\Flagged", flagsRequest.Flagged),
            ("\\Deleted", flagsRequest.Deleted),
            ("\\Draft", flagsRequest.Draft)
        };

        foreach (var (flagName, value) in standardFlags) {
            if (value.HasValue) {
                await SetMessageFlagAsync(userId, messageId, flagName, value.Value);
            }
        }

        // Handle custom flags
        if (flagsRequest.CustomFlags != null) {
            // Remove existing custom flags not in the new list
            var existingCustomFlags = await _context.MessageFlags
                .Where(mf => mf.MessageId == messageId && mf.UserId == userId &&
                           !mf.FlagName.StartsWith("\\"))
                .ToListAsync();

            foreach (var flag in existingCustomFlags) {
                if (!flagsRequest.CustomFlags.Contains(flag.FlagName)) {
                    flag.IsSet = false;
                    flag.ModifiedAt = DateTime.UtcNow;
                }
            }

            // Add new custom flags
            foreach (var customFlag in flagsRequest.CustomFlags) {
                await SetMessageFlagAsync(userId, messageId, customFlag, true);
            }
        }
    }

    private async Task SetMessageFlagAsync(int userId, int messageId, string flagName, bool isSet) {
        var flag = await _context.MessageFlags
            .Where(mf => mf.MessageId == messageId && mf.UserId == userId && mf.FlagName == flagName)
            .FirstOrDefaultAsync();

        if (flag == null) {
            _context.MessageFlags.Add(new MessageFlag {
                MessageId = messageId,
                UserId = userId,
                FlagName = flagName,
                IsSet = isSet
            });
        } else {
            flag.IsSet = isSet;
            flag.ModifiedAt = DateTime.UtcNow;
        }
    }

    private bool IsDraftMessage(int userId, int messageId) {
        return _context.MessageFlags
            .Any(mf => mf.MessageId == messageId && mf.UserId == userId &&
                      mf.FlagName == "\\Draft" && mf.IsSet);
    }

    private async Task<string> GetUserEmailAsync(int userId) {
        var user = await _context.Users
            .Include(u => u.Domain)
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync();

        return user != null ? $"{user.Username}@{user.Domain.Name}" : "unknown@localhost";
    }

    private async Task<int> GetNextUidAsync() {
        var lastMessage = await _context.Messages
            .OrderByDescending(m => m.Uid)
            .FirstOrDefaultAsync();

        return (lastMessage?.Uid ?? 0) + 1;
    }

    private string FormatMessageSize(int bytes) {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private MessageEnvelopeResponse ParseEnvelope(string? envelopeJson) {
        if (string.IsNullOrEmpty(envelopeJson)) {
            return new MessageEnvelopeResponse();
        }

        try {
            return JsonSerializer.Deserialize<MessageEnvelopeResponse>(envelopeJson) ?? new MessageEnvelopeResponse();
        } catch {
            return new MessageEnvelopeResponse();
        }
    }

    private MessageBodyStructureResponse ParseBodyStructure(string? bodyStructureJson) {
        if (string.IsNullOrEmpty(bodyStructureJson)) {
            return new MessageBodyStructureResponse { Type = "text", Subtype = "plain" };
        }

        try {
            return JsonSerializer.Deserialize<MessageBodyStructureResponse>(bodyStructureJson) ??
                   new MessageBodyStructureResponse { Type = "text", Subtype = "plain" };
        } catch {
            return new MessageBodyStructureResponse { Type = "text", Subtype = "plain" };
        }
    }

    private string BuildHeaders(string messageId, MessageRequest request) {
        var headers = new List<string> {
            $"Message-ID: {messageId}",
            $"Date: {DateTime.UtcNow:r}",
            $"From: {request.ToAddress}",
            $"To: {request.ToAddress}",
            $"Subject: {request.Subject}"
        };

        if (!string.IsNullOrEmpty(request.CcAddress)) {
            headers.Add($"CC: {request.CcAddress}");
        }

        if (!string.IsNullOrEmpty(request.InReplyTo)) {
            headers.Add($"In-Reply-To: {request.InReplyTo}");
        }

        if (!string.IsNullOrEmpty(request.References)) {
            headers.Add($"References: {request.References}");
        }

        headers.Add("MIME-Version: 1.0");
        headers.Add("Content-Type: text/plain; charset=utf-8");
        headers.Add("Content-Transfer-Encoding: 8bit");

        return string.Join("\r\n", headers) + "\r\n";
    }

    private int CalculateMessageSize(MessageRequest request) {
        var size = (request.Subject?.Length ?? 0) +
                   (request.Body?.Length ?? 0) +
                   (request.BodyHtml?.Length ?? 0) +
                   200; // Approximate header size
        return size;
    }

    private int CalculateMessageSize(Frimerki.Models.Entities.Message message) {
        return (message.Subject?.Length ?? 0) +
               (message.Body?.Length ?? 0) +
               (message.BodyHtml?.Length ?? 0) +
               message.Headers.Length;
    }

    private string BuildEnvelopeJson(MessageRequest request, string fromEmail) {
        var envelope = new MessageEnvelopeResponse {
            Date = DateTime.UtcNow.ToString("r"),
            Subject = request.Subject,
            From = new List<MessageAddressResponse> { new() { Email = fromEmail } },
            ReplyTo = new List<MessageAddressResponse> { new() { Email = fromEmail } },
            To = new List<MessageAddressResponse> { new() { Email = request.ToAddress } },
            MessageId = $"<{Guid.NewGuid()}@{DateTime.UtcNow:yyyyMMddHHmmss}>"
        };

        if (!string.IsNullOrEmpty(request.CcAddress)) {
            envelope.Cc = request.CcAddress.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(email => new MessageAddressResponse { Email = email.Trim() })
                .ToList();
        }

        return JsonSerializer.Serialize(envelope);
    }

    private string BuildBodyStructureJson(MessageRequest request) {
        var bodyStructure = new MessageBodyStructureResponse {
            Type = "text",
            Subtype = !string.IsNullOrEmpty(request.BodyHtml) ? "html" : "plain",
            Parameters = new Dictionary<string, string> { { "charset", "utf-8" } },
            ContentTransferEncoding = "8bit",
            Size = request.Body?.Length ?? 0
        };

        return JsonSerializer.Serialize(bodyStructure);
    }
}
