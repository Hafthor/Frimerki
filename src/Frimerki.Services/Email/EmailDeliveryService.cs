using System.Text;
using System.Text.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Common;
using Frimerki.Services.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Frimerki.Services.Email;

/// <summary>
/// Service for delivering incoming emails to user mailboxes
/// </summary>
public class EmailDeliveryService(
    EmailDbContext context,
    IUserService userService,
    INowProvider nowProvider,
    ILogger<EmailDeliveryService> logger) {
    /// <summary>
    /// Deliver an incoming email to the appropriate recipient(s)
    /// </summary>
    public async Task<bool> DeliverEmailAsync(string fromAddress, List<string> toAddresses, string messageData) {
        try {
            logger.LogInformation("Delivering email from {FromAddress} to {ToCount} recipients",
                fromAddress, toAddresses.Count);

            // Parse the message data using MimeKit
            var mimeMessage = await ParseMimeMessageAsync(messageData);
            if (mimeMessage == null) {
                logger.LogError("Failed to parse incoming message");
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
                logger.LogInformation("Email delivered successfully to {SuccessCount}/{TotalCount} recipients",
                    deliveryResults.Count(r => r), deliveryResults.Count);
            } else {
                logger.LogWarning("Email delivery failed for all recipients");
            }

            return success;
        } catch (Exception ex) {
            logger.LogError(ex, "Error delivering email from {FromAddress}", fromAddress);
            return false;
        }
    }

    private async Task<MimeMessage> ParseMimeMessageAsync(string messageData) {
        try {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(messageData));
            return await MimeMessage.LoadAsync(stream);
        } catch (Exception ex) {
            logger.LogError(ex, "Error parsing MIME message");
            return null;
        }
    }

    private async Task<bool> DeliverToRecipientAsync(string fromAddress, string toAddress, MimeMessage mimeMessage, string rawMessageData) {
        try {
            // Find the recipient user
            var user = await userService.GetUserEntityByEmailAsync(toAddress);
            if (user == null) {
                logger.LogWarning("Recipient {ToAddress} not found, message not delivered", toAddress);
                return false;
            }

            if (!user.CanReceive) {
                logger.LogWarning("Recipient {ToAddress} cannot receive mail, message not delivered", toAddress);
                return false;
            }

            // Get user's INBOX folder
            var inboxFolder = await context.Folders
                .Where(f => f.UserId == user.Id && f.SystemFolderType == "INBOX")
                .FirstOrDefaultAsync();

            if (inboxFolder == null) {
                logger.LogError("INBOX folder not found for user {UserId}", user.Id);
                return false;
            }

            // Generate unique UID for this folder
            var uid = await GetNextUidAsync(inboxFolder);
            var messageId = mimeMessage.MessageId ?? $"<{Guid.NewGuid()}@{nowProvider.UtcNow:yyyyMMddHHmmss}>";

            // Create the message entity
            var message = new Frimerki.Models.Entities.Message {
                HeaderMessageId = messageId,
                FromAddress = fromAddress,
                ToAddress = toAddress,
                CcAddress = ExtractAddresses(mimeMessage.Cc),
                BccAddress = ExtractAddresses(mimeMessage.Bcc),
                Subject = mimeMessage.Subject ?? "",
                Body = mimeMessage.TextBody ?? "",
                BodyHtml = mimeMessage.HtmlBody,
                Headers = ExtractHeaders(rawMessageData),
                MessageSize = rawMessageData.Length,
                SentDate = mimeMessage.Date.DateTime,
                ReceivedAt = nowProvider.UtcNow,
                InReplyTo = mimeMessage.InReplyTo,
                References = string.Join(" ", mimeMessage.References),
                Uid = uid,
                UidValidity = 1,
                Envelope = BuildEnvelopeJson(mimeMessage),
                BodyStructure = BuildBodyStructureJson(mimeMessage)
            };

            context.Messages.Add(message);
            await context.SaveChangesAsync();

            // Create UserMessage relationship for the recipient
            var userMessage = new UserMessage {
                UserId = user.Id,
                MessageId = message.Id,
                FolderId = inboxFolder.Id,
                Uid = uid,
                ReceivedAt = nowProvider.UtcNow
            };

            context.UserMessages.Add(userMessage);

            // Set default flags for incoming mail (mark as unread/recent)
            var recentFlag = new MessageFlag {
                MessageId = message.Id,
                UserId = user.Id,
                FlagName = "\\Recent",
                IsSet = true
            };

            context.MessageFlags.Add(recentFlag);

            // Update folder statistics
            inboxFolder.Exists++;
            inboxFolder.Recent++;
            inboxFolder.Unseen++; // Since we didn't set \Seen flag
            inboxFolder.UidNext = uid + 1;

            await context.SaveChangesAsync();

            logger.LogInformation("Delivered message {MessageId} to user {UserId} in INBOX",
                message.Id, user.Id);

            return true;
        } catch (Exception ex) {
            logger.LogError(ex, "Error delivering message to {ToAddress}", toAddress);
            return false;
        }
    }

    private async Task<int> GetNextUidAsync(Frimerki.Models.Entities.Folder folder) {
        // Get the next UID for this folder and increment UidNext
        var currentUid = folder.UidNext;
        folder.UidNext++;

        // Save the updated folder to persist the incremented UidNext
        await context.SaveChangesAsync();

        return currentUid;
    }

    private string ExtractHeaders(string rawMessageData) {
        // Extract headers from raw message data (everything before first empty line)
        var headerLines = rawMessageData.Split('\n')
            .TakeWhile(line => !string.IsNullOrWhiteSpace(line.TrimEnd('\r')))
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        return string.Join("\r\n", headerLines) + "\r\n";
    }

    private string ExtractAddresses(InternetAddressList addressList) {
        if (!(addressList?.Count > 0)) {
            return "";
        }
        return string.Join(", ", addressList);
    }

    private string BuildEnvelopeJson(MimeMessage mimeMessage) {
        var envelope = new MessageEnvelopeResponse {
            Date = mimeMessage.Date.ToString("r"),
            Subject = mimeMessage.Subject ?? "",
            From = [.. mimeMessage.From.Select(addr => new MessageAddressResponse(addr))],
            ReplyTo = [.. mimeMessage.ReplyTo.Select(addr => new MessageAddressResponse(addr))],
            To = [.. mimeMessage.To.Select(addr => new MessageAddressResponse(addr))],
            MessageId = mimeMessage.MessageId ?? $"<{Guid.NewGuid()}@{nowProvider.UtcNow:yyyyMMddHHmmss}>",
            Cc = mimeMessage.Cc?.Count > 0 ? [.. mimeMessage.Cc.Select(addr => new MessageAddressResponse(addr))] : null
        };

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
