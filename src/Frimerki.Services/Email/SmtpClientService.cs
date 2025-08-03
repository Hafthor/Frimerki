using System.Net;
using System.Net.Mail;
using System.Text;
using Frimerki.Models.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Email;

/// <summary>
/// Service for sending outbound emails via SMTP
/// </summary>
public class SmtpClientService(IConfiguration configuration, ILogger<SmtpClientService> logger) {
    /// <summary>
    /// Send an email message via SMTP
    /// </summary>
    public async Task<bool> SendEmailAsync(MessageRequest request, string fromAddress) {
        try {
            using var message = new MailMessage();

            // Set sender
            message.From = new MailAddress(fromAddress);

            // Add recipients
            foreach (var recipient in request.ToAddress.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                message.To.Add(new MailAddress(recipient.Trim()));
            }

            // Set subject and body
            message.Subject = request.Subject;
            message.Body = request.Body;
            message.IsBodyHtml = request.IsHtml;

            // Set encoding
            message.SubjectEncoding = Encoding.UTF8;
            message.BodyEncoding = Encoding.UTF8;

            // Add headers
            message.Headers.Add("X-Mailer", "Frimerki Email Server");
            message.Headers.Add("Message-ID", $"<{Guid.NewGuid()}@frimerki.local>");

            // Handle attachments if any
            if (request.Attachments?.Count > 0) {
                foreach (var attachment in request.Attachments) {
                    var mailAttachment = new Attachment(
                        new MemoryStream(attachment.Content),
                        attachment.Name,
                        attachment.ContentType);
                    message.Attachments.Add(mailAttachment);
                }
            }

            // Get SMTP configuration
            var smtpHost = configuration["Smtp:Host"] ?? "localhost";
            var smtpPort = int.Parse(configuration["Smtp:Port"] ?? "25");
            var smtpUsername = configuration["Smtp:Username"];
            var smtpPassword = configuration["Smtp:Password"];
            var enableSsl = bool.Parse(configuration["Smtp:EnableSsl"] ?? "false");

            using var smtpClient = new SmtpClient(smtpHost, smtpPort);

            // Configure authentication if provided
            if (!string.IsNullOrEmpty(smtpUsername) && !string.IsNullOrEmpty(smtpPassword)) {
                smtpClient.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
            }

            smtpClient.EnableSsl = enableSsl;
            smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
            smtpClient.Timeout = 30000; // 30 seconds

            await smtpClient.SendMailAsync(message);

            logger.LogInformation("Email sent successfully from {From} to {To}",
                fromAddress, request.ToAddress);

            return true;
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to send email from {From} to {To}",
                fromAddress, request.ToAddress);
            return false;
        }
    }

    /// <summary>
    /// Send a simple text email
    /// </summary>
    public async Task<bool> SendSimpleEmailAsync(string from, string to, string subject, string body) {
        var request = new MessageRequest {
            ToAddress = to,
            Subject = subject,
            Body = body,
            IsHtml = false
        };

        return await SendEmailAsync(request, from);
    }

    /// <summary>
    /// Send an HTML email
    /// </summary>
    public async Task<bool> SendHtmlEmailAsync(string from, string to, string subject, string htmlBody) {
        var request = new MessageRequest {
            ToAddress = to,
            Subject = subject,
            Body = htmlBody,
            IsHtml = true
        };

        return await SendEmailAsync(request, from);
    }

    /// <summary>
    /// Validate email configuration
    /// </summary>
    public bool ValidateConfiguration() {
        try {
            var smtpHost = configuration["Smtp:Host"];
            var smtpPort = configuration["Smtp:Port"];

            if (string.IsNullOrEmpty(smtpHost)) {
                logger.LogWarning("SMTP Host not configured");
                return false;
            }

            if (!int.TryParse(smtpPort, out var port) || port is <= 0 or > 65535) {
                logger.LogWarning("Invalid SMTP port configuration: {Port}", smtpPort);
                return false;
            }

            return true;
        } catch (Exception ex) {
            logger.LogError(ex, "Error validating SMTP configuration");
            return false;
        }
    }
}
