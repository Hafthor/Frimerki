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
public class SmtpClientService {
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpClientService> _logger;

    public SmtpClientService(IConfiguration configuration, ILogger<SmtpClientService> logger) {
        _configuration = configuration;
        _logger = logger;
    }

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
            message.Headers.Add("X-Mailer", "Frímerki Email Server");
            message.Headers.Add("Message-ID", $"<{Guid.NewGuid()}@frímerki.local>");

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
            var smtpHost = _configuration["Smtp:Host"] ?? "localhost";
            var smtpPort = int.Parse(_configuration["Smtp:Port"] ?? "25");
            var smtpUsername = _configuration["Smtp:Username"];
            var smtpPassword = _configuration["Smtp:Password"];
            var enableSsl = bool.Parse(_configuration["Smtp:EnableSsl"] ?? "false");

            using var smtpClient = new SmtpClient(smtpHost, smtpPort);

            // Configure authentication if provided
            if (!string.IsNullOrEmpty(smtpUsername) && !string.IsNullOrEmpty(smtpPassword)) {
                smtpClient.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
            }

            smtpClient.EnableSsl = enableSsl;
            smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
            smtpClient.Timeout = 30000; // 30 seconds

            await smtpClient.SendMailAsync(message);

            _logger.LogInformation("Email sent successfully from {From} to {To}",
                fromAddress, request.ToAddress);

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to send email from {From} to {To}",
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
            var smtpHost = _configuration["Smtp:Host"];
            var smtpPort = _configuration["Smtp:Port"];

            if (string.IsNullOrEmpty(smtpHost)) {
                _logger.LogWarning("SMTP Host not configured");
                return false;
            }

            if (!int.TryParse(smtpPort, out var port) || port <= 0 || port > 65535) {
                _logger.LogWarning("Invalid SMTP port configuration: {Port}", smtpPort);
                return false;
            }

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error validating SMTP configuration");
            return false;
        }
    }
}
