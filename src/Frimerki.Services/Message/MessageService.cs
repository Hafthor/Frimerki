using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Text.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Message;

public class MessageService : IMessageService {
    private readonly EmailDbContext _context;
    private readonly INowProvider _nowProvider;
    private readonly ILogger<MessageService> _logger;

    // Standard IMAP flag names
    private const string SeenFlag = "\\Seen";
    private const string AnsweredFlag = "\\Answered";
    private const string FlaggedFlag = "\\Flagged";
    private const string DeletedFlag = "\\Deleted";
    private const string DraftFlag = "\\Draft";
    private const string RecentFlag = "\\Recent";

    // Standard IMAP flags with their corresponding properties in MessageFlagsRequest
    public static readonly FrozenDictionary<string, Func<MessageFlagsRequest, bool?>> StandardFlags =
        new Dictionary<string, Func<MessageFlagsRequest, bool?>> {
            [SeenFlag] = req => req.Seen,
            [AnsweredFlag] = req => req.Answered,
            [FlaggedFlag] = req => req.Flagged,
            [DeletedFlag] = req => req.Deleted,
            [DraftFlag] = req => req.Draft,
            //[RecentFlag] = req => req.Recent, // Not included on purpose?
        }.ToFrozenDictionary();

    public MessageService(EmailDbContext context, INowProvider nowProvider, ILogger<MessageService> logger) {
        _context = context;
        _nowProvider = nowProvider;
        _logger = logger;
    }

    public async Task<PaginatedInfo<MessageListItemResponse>> GetMessagesAsync(int userId, MessageFilterRequest request) {
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
            var searchTerm = request.Q;
            query = query.Where(um =>
                (um.Message.Subject != null && um.Message.Subject.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (um.Message.Body != null && um.Message.Body.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                um.Message.FromAddress.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
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
                Subject = um.Message.Subject ?? "",
                FromAddress = um.Message.FromAddress,
                ToAddress = um.Message.ToAddress ?? "",
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

        var response = new PaginatedInfo<MessageListItemResponse> {
            Items = messages,
            Skip = request.Skip,
            Take = take,
            TotalCount = totalCount,
            NextUrl = nextUrl,
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
            Subject = message.Subject ?? "",
            FromAddress = message.FromAddress,
            ToAddress = message.ToAddress ?? "",
            CcAddress = message.CcAddress,
            BccAddress = message.BccAddress,
            SentDate = message.SentDate ?? message.ReceivedAt,
            ReceivedAt = message.ReceivedAt,
            MessageSize = message.MessageSize,
            Body = message.Body ?? "",
            BodyHtml = message.BodyHtml,
            Headers = message.Headers,
            Envelope = envelope,
            BodyStructure = bodyStructure,
            Flags = flags,
            Attachments = message.Attachments.Select(a => new MessageAttachmentResponse {
                FileName = a.FileName,
                ContentType = a.ContentType ?? "application/octet-stream",
                Size = a.Size,
                SizeFormatted = FormatMessageSize(a.Size),
                Path = a.FilePath ?? ""
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
        var messageId = $"<{Guid.NewGuid()}@{_nowProvider.UtcNow:yyyyMMddHHmmss}>";

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
            SentDate = _nowProvider.UtcNow,
            ReceivedAt = _nowProvider.UtcNow,
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
            ReceivedAt = _nowProvider.UtcNow
        };

        _context.UserMessages.Add(userMessage);

        // Set default flags (mark as seen since user is sending it)
        var seenFlag = new MessageFlag {
            MessageId = message.Id,
            UserId = userId,
            FlagName = SeenFlag,
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
            .Where(mf => mf.MessageId == messageId && mf.UserId == userId && mf.FlagName == DeletedFlag)
            .FirstOrDefaultAsync();

        if (deletedFlag == null) {
            _context.MessageFlags.Add(new MessageFlag {
                MessageId = messageId,
                UserId = userId,
                FlagName = DeletedFlag,
                IsSet = true
            });
        } else {
            deletedFlag.IsSet = true;
            deletedFlag.ModifiedAt = _nowProvider.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Moved message {MessageId} to trash for user {UserId}", messageId, userId);
        return true;
    }

    // Helper methods
    private IQueryable<UserMessage> ApplyFlagFiltering(IQueryable<UserMessage> query, int userId, string flags) {
        (string? name, bool set) flag = flags.ToLower() switch {
            "read" or "seen" => (SeenFlag, true),
            "unread" or "unseen" => (SeenFlag, false),
            "flagged" => (FlaggedFlag, true),
            "answered" => (AnsweredFlag, true),
            "draft" => (DraftFlag, true),
            "deleted" => (DeletedFlag, true),
            _ => (null, true)
        };
        if (flag.name == null) {
            return query;
        }
        return query.Where(um => um.Message.MessageFlags
            .Any(mf => mf.UserId == userId && mf.FlagName == flag.name && mf.IsSet) == flag.set);
    }

    private IQueryable<UserMessage> ApplySorting(IQueryable<UserMessage> query, string sortBy, string sortOrder) {
        var isDescending = sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);

        return sortBy.ToLower() switch {
            "subject" => OrderBy(um => um.Message.Subject),
            "sender" or "from" => OrderBy(um => um.Message.FromAddress),
            "size" => OrderBy(um => um.Message.MessageSize),
            _ => OrderBy(um => um.Message.SentDate ?? um.Message.ReceivedAt) // default to date
        };

        IOrderedQueryable<UserMessage> OrderBy<T>(Expression<Func<UserMessage, T>> func) =>
            isDescending ? query.OrderByDescending(func) : query.OrderBy(func);
    }

    private string BuildNextUrl(MessageFilterRequest request, int nextSkip) {
        Dictionary<string, string> query = new() {
            ["skip"] = $"{nextSkip}",
            ["take"] = $"{request.Take}",
        };

        if (!string.IsNullOrEmpty(request.Q)) {
            query["q"] = request.Q;
        }

        if (!string.IsNullOrEmpty(request.Folder)) {
            query["folder"] = request.Folder;
        }

        if (request.FolderId.HasValue) {
            query["folderId"] = $"{request.FolderId.Value}";
        }

        if (!string.IsNullOrEmpty(request.Flags)) {
            query["flags"] = request.Flags;
        }

        if (!string.IsNullOrEmpty(request.From)) {
            query["from"] = request.From;
        }

        if (!string.IsNullOrEmpty(request.To)) {
            query["to"] = request.To;
        }

        if (request.Since.HasValue) {
            query["since"] = FormatDateTime(request.Since.Value);
        }

        if (request.Before.HasValue) {
            query["before"] = FormatDateTime(request.Before.Value);
        }

        if (request.MinSize.HasValue) {
            query["minSize"] = $"{request.MinSize.Value}";
        }

        if (request.MaxSize.HasValue) {
            query["maxSize"] = $"{request.MaxSize.Value}";
        }

        if (request.SortBy != "date") {
            query["sortBy"] = request.SortBy;
        }

        if (request.SortOrder != "desc") {
            query["sortOrder"] = request.SortOrder;
        }

        return $"/api/messages?{string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)))}";
    }

    private string? FormatDateTime(DateTime? dt) {
        return dt.HasValue ? FormatDateTime(dt.Value) : null;
    }

    private string FormatDateTime(DateTime dt) {
        if (dt.Microsecond != 0) {
            return $"{dt:yyyy-MM-ddTHH:mm:ss.tttttt}";
        } else if (dt.Second != 0) {
            return $"{dt:yyyy-MM-ddTHH:mm:ss}";
        } else if (dt.Minute != 0 || dt.Hour != 0) {
            return $"{dt:yyyy-MM-ddTHH:mm}";
        } else {
            return $"{dt:yyyy-MM-dd}";
        }
    }

    private Dictionary<string, object> BuildAppliedFilters(MessageFilterRequest request) {
        var filters = new Dictionary<string, object>();

        AddStringFilter(filters, "q", request.Q);
        AddStringFilter(filters, "folder", request.Folder);
        AddFilter(filters, "folderId", request.FolderId);
        AddStringFilter(filters, "flags", request.Flags);
        AddStringFilter(filters, "from", request.From);
        AddStringFilter(filters, "to", request.To);
        AddFilter(filters, "since", FormatDateTime(request.Since));
        AddFilter(filters, "before", FormatDateTime(request.Before));
        AddFilter(filters, "minSize", request.MinSize);
        AddFilter(filters, "maxSize", request.MaxSize);

        return filters;
    }

    private static void AddStringFilter(Dictionary<string, object> filters, string key, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            filters[key] = value;
        }
    }

    private static void AddFilter<T>(Dictionary<string, object> filters, string key, T? value) where T : struct {
        if (value.HasValue) {
            filters[key] = value.Value;
        }
    }

    private static void AddFilter(Dictionary<string, object> filters, string key, string? value) {
        if (value != null) {
            filters[key] = value;
        }
    }

    private MessageFlagsResponse GetMessageFlags(IEnumerable<MessageFlag> flags) {
        var flagDict = flags.Where(f => f.IsSet).ToDictionary(f => f.FlagName, f => f.IsSet);

        return new MessageFlagsResponse {
            Seen = flagDict.GetValueOrDefault(SeenFlag, false),
            Answered = flagDict.GetValueOrDefault(AnsweredFlag, false),
            Flagged = flagDict.GetValueOrDefault(FlaggedFlag, false),
            Deleted = flagDict.GetValueOrDefault(DeletedFlag, false),
            Draft = flagDict.GetValueOrDefault(DraftFlag, false),
            Recent = flagDict.GetValueOrDefault(RecentFlag, false),
            CustomFlags = flags.Where(f => f.IsSet && !StandardFlags.ContainsKey(f.FlagName) && f.FlagName != RecentFlag)
                .Select(f => f.FlagName).ToList()
        };
    }

    private async Task UpdateMessageFlagsAsync(int userId, int messageId, MessageFlagsRequest flagsRequest) {
        foreach (var (flagName, valueExtractor) in StandardFlags) {
            var value = valueExtractor(flagsRequest);
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
                    flag.ModifiedAt = _nowProvider.UtcNow;
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
            flag.ModifiedAt = _nowProvider.UtcNow;
        }
    }

    private bool IsDraftMessage(int userId, int messageId) {
        return _context.MessageFlags
            .Any(mf => mf.MessageId == messageId && mf.UserId == userId &&
                      mf.FlagName == DraftFlag && mf.IsSet);
    }

    private async Task<string> GetUserEmailAsync(int userId) {
        var user = await _context.Users
            .Include(u => u.Domain)
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync();

        return user != null ? $"{user.Username}@{user.Domain.Name}" : $"unknown{userId}@localhost";
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
            $"Date: {_nowProvider.UtcNow:r}",
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
            Date = _nowProvider.UtcNow.ToString("r"),
            Subject = request.Subject,
            From = new List<MessageAddressResponse> { new() { Email = fromEmail } },
            ReplyTo = new List<MessageAddressResponse> { new() { Email = fromEmail } },
            To = new List<MessageAddressResponse> { new() { Email = request.ToAddress } },
            MessageId = $"<{Guid.NewGuid()}@{_nowProvider.UtcNow:yyyyMMddHHmmss}>"
        };

        if (!string.IsNullOrEmpty(request.CcAddress)) {
            envelope.Cc = [.. request.CcAddress.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(email => new MessageAddressResponse { Email = email.Trim() })];
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
