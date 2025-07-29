using System.Text;
using System.Text.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Frimerki.Services.Email;

/// <summary>
/// Service for delivering incoming emails to user mailboxes
/// </summary>
public class EmailDeliveryService {
    private readonly EmailDbContext _context;
    private readonly IUserService _userService;
    private readonly ILogger<EmailDeliveryService> _logger;

    public EmailDeliveryService(
        EmailDbContext context,
        IUserService userService,
        ILogger<EmailDeliveryService> logger) {
        _context = context;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Deliver an incoming email to the appropriate recipient(s)
    /// </summary>
    public async Task<bool> DeliverEmailAsync(string fromAddress, List<string> toAddresses, string messageData) {
        try {
            _logger.LogInformation("Delivering email from {FromAddress} to {ToCount} recipients",
                fromAddress, toAddresses.Count);

            // Parse the message data using MimeKit
            var mimeMessage = await ParseMimeMessageAsync(messageData);
            if (mimeMessage == null) {
                _logger.LogError("Failed to parse incoming message");
                return false;
            }

            // Process each recipient
            var deliveryResults = new List<bool>();
            foreach (var toAddress in toAddresses) {
                var result = await DeliverToRecipientAsync(fromAddress, toAddress, mimeMessage, messageData);
                deliveryResults.Add(result);
            }

            // Consider delivery successful if at least one recipient received the message
            var success = deliveryResults.Any(r => r);

            if (success) {
                _logger.LogInformation("Email delivered successfully to {SuccessCount}/{TotalCount} recipients",
                    deliveryResults.Count(r => r), deliveryResults.Count);
            } else {
                _logger.LogWarning("Email delivery failed for all recipients");
            }

            return success;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error delivering email from {FromAddress}", fromAddress);
            return false;
        }
    }

    private async Task<MimeMessage?> ParseMimeMessageAsync(string messageData) {
        try {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(messageData));
            return await MimeMessage.LoadAsync(stream);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error parsing MIME message");
            return null;
        }
    }

    private async Task<bool> DeliverToRecipientAsync(string fromAddress, string toAddress, MimeMessage mimeMessage, string rawMessageData) {
        try {
            // Find the recipient user
            var user = await _userService.GetUserEntityByEmailAsync(toAddress);
            if (user == null) {
                _logger.LogWarning("Recipient {ToAddress} not found, message not delivered", toAddress);
                return false;
            }

            if (!user.CanReceive) {
                _logger.LogWarning("Recipient {ToAddress} cannot receive mail, message not delivered", toAddress);
                return false;
            }

            // Get user's INBOX folder
            var inboxFolder = await _context.Folders
                .Where(f => f.UserId == user.Id && f.SystemFolderType == "INBOX")
                .FirstOrDefaultAsync();

            if (inboxFolder == null) {
                _logger.LogError("INBOX folder not found for user {UserId}", user.Id);
                return false;
            }

            // Generate unique UID for this folder
            var uid = await GetNextUidAsync(inboxFolder);
            var messageId = mimeMessage.MessageId ?? $"<{Guid.NewGuid()}@{DateTime.UtcNow:yyyyMMddHHmmss}>";

            // Extract message content
            var subject = mimeMessage.Subject ?? string.Empty;
            var textBody = mimeMessage.TextBody ?? string.Empty;
            var htmlBody = mimeMessage.HtmlBody;

            // Build headers from the raw message
            var headers = ExtractHeaders(rawMessageData);

            // Create the message entity
            var message = new Frimerki.Models.Entities.Message {
                HeaderMessageId = messageId,
                FromAddress = fromAddress,
                ToAddress = toAddress,
                CcAddress = ExtractCcAddresses(mimeMessage),
                BccAddress = ExtractBccAddresses(mimeMessage),
                Subject = subject,
                Body = textBody,
                BodyHtml = htmlBody,
                Headers = headers,
                MessageSize = rawMessageData.Length,
                SentDate = mimeMessage.Date.DateTime,
                ReceivedAt = DateTime.UtcNow,
                InReplyTo = mimeMessage.InReplyTo,
                References = string.Join(" ", mimeMessage.References),
                Uid = uid,
                UidValidity = 1,
                Envelope = BuildEnvelopeJson(mimeMessage),
                BodyStructure = BuildBodyStructureJson(mimeMessage)
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Create UserMessage relationship for the recipient
            var userMessage = new UserMessage {
                UserId = user.Id,
                MessageId = message.Id,
                FolderId = inboxFolder.Id,
                Uid = uid,
                ReceivedAt = DateTime.UtcNow
            };

            _context.UserMessages.Add(userMessage);

            // Set default flags for incoming mail (mark as unread/recent)
            var recentFlag = new MessageFlag {
                MessageId = message.Id,
                UserId = user.Id,
                FlagName = "\\Recent",
                IsSet = true
            };

            _context.MessageFlags.Add(recentFlag);

            // Update folder statistics
            inboxFolder.Exists += 1;
            inboxFolder.Recent += 1;
            inboxFolder.Unseen += 1; // Since we didn't set \Seen flag
            inboxFolder.UidNext = uid + 1;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Delivered message {MessageId} to user {UserId} in INBOX",
                message.Id, user.Id);

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error delivering message to {ToAddress}", toAddress);
            return false;
        }
    }

    private async Task<int> GetNextUidAsync(Frimerki.Models.Entities.Folder folder) {
        // Get the next UID for this folder and increment UidNext
        var currentUid = folder.UidNext;
        folder.UidNext++;

        // Save the updated folder to persist the incremented UidNext
        await _context.SaveChangesAsync();

        return currentUid;
    }

    private string ExtractHeaders(string rawMessageData) {
        // Extract headers from raw message data (everything before first empty line)
        var lines = rawMessageData.Split('\n');
        var headerLines = new List<string>();

        foreach (var line in lines) {
            if (string.IsNullOrWhiteSpace(line.TrimEnd('\r'))) {
                break; // Empty line marks end of headers
            }
            headerLines.Add(line.TrimEnd('\r'));
        }

        return string.Join("\r\n", headerLines) + "\r\n";
    }

    private string? ExtractCcAddresses(MimeMessage mimeMessage) {
        if (mimeMessage.Cc?.Count > 0) {
            return string.Join(", ", mimeMessage.Cc.Select(addr => addr.ToString()));
        }
        return null;
    }

    private string? ExtractBccAddresses(MimeMessage mimeMessage) {
        if (mimeMessage.Bcc?.Count > 0) {
            return string.Join(", ", mimeMessage.Bcc.Select(addr => addr.ToString()));
        }
        return null;
    }

    private string BuildEnvelopeJson(MimeMessage mimeMessage) {
        var envelope = new MessageEnvelopeResponse {
            Date = mimeMessage.Date.ToString("r"),
            Subject = mimeMessage.Subject ?? string.Empty,
            From = mimeMessage.From.Select(addr => new MessageAddressResponse {
                Email = addr is MailboxAddress mb ? mb.Address : addr.ToString()
            }).ToList(),
            ReplyTo = mimeMessage.ReplyTo.Select(addr => new MessageAddressResponse {
                Email = addr is MailboxAddress mb ? mb.Address : addr.ToString()
            }).ToList(),
            To = mimeMessage.To.Select(addr => new MessageAddressResponse {
                Email = addr is MailboxAddress mb ? mb.Address : addr.ToString()
            }).ToList(),
            MessageId = mimeMessage.MessageId ?? $"<{Guid.NewGuid()}@{DateTime.UtcNow:yyyyMMddHHmmss}>"
        };

        if (mimeMessage.Cc?.Count > 0) {
            envelope.Cc = mimeMessage.Cc.Select(addr => new MessageAddressResponse {
                Email = addr is MailboxAddress mb ? mb.Address : addr.ToString()
            }).ToList();
        }

        return JsonSerializer.Serialize(envelope);
    }

    private string BuildBodyStructureJson(MimeMessage mimeMessage) {
        var bodyStructure = new MessageBodyStructureResponse {
            Type = "text",
            Subtype = !string.IsNullOrEmpty(mimeMessage.HtmlBody) ? "html" : "plain",
            Parameters = new Dictionary<string, string> { { "charset", "utf-8" } },
            ContentTransferEncoding = "8bit",
            Size = (mimeMessage.TextBody?.Length ?? 0) + (mimeMessage.HtmlBody?.Length ?? 0)
        };

        return JsonSerializer.Serialize(bodyStructure);
    }
}
