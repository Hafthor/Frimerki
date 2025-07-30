using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Frimerki.Models.DTOs;
using Frimerki.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Frimerki.Tests.Services.Email;

public class SmtpClientServiceTests {
    private readonly ILogger<SmtpClientService> _logger;

    public SmtpClientServiceTests() {
        _logger = NullLogger<SmtpClientService>.Instance;
    }

    private SmtpClientService CreateService(Dictionary<string, string?> configValues) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new SmtpClientService(configuration, _logger);
    }

    [Fact]
    public void ValidateConfiguration_ValidConfiguration_ReturnsTrue() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "587"
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateConfiguration_MissingHost_ReturnsFalse() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Port"] = "587"
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateConfiguration_EmptyHost_ReturnsFalse() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "",
            ["Smtp:Port"] = "587"
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateConfiguration_NullHost_ReturnsFalse() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = null,
            ["Smtp:Port"] = "587"
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("99999")]
    public void ValidateConfiguration_InvalidPort_ReturnsFalse(string port) {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = port
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("abc")]
    [InlineData("25.5")]
    [InlineData("")]
    public void ValidateConfiguration_NonNumericPort_ReturnsFalse(string port) {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = port
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("587")]
    [InlineData("465")]
    [InlineData("2525")]
    [InlineData("1")]
    [InlineData("65535")]
    public void ValidateConfiguration_ValidPorts_ReturnsTrue(string port) {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = port
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateConfiguration_MissingPort_ReturnsFalse() {
        // Arrange - missing port should fail validation even though SendEmail would default to 25
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "smtp.example.com"
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("smtp.gmail.com")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("mail.company.co.uk")]
    [InlineData("smtp-relay.example.org")]
    public void ValidateConfiguration_VariousValidHosts_ReturnsTrue(string host) {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = host,
            ["Smtp:Port"] = "587"
        };
        var service = CreateService(config);

        // Act
        var result = service.ValidateConfiguration();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SendSimpleEmailAsync_CallsSendEmailAsync() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        // Note: This will fail to actually send since localhost:25 likely isn't running,
        // but we're testing that the method doesn't throw and returns false on failure

        // Act
        var result = await service.SendSimpleEmailAsync(
            "test@example.com",
            "recipient@example.com",
            "Test Subject",
            "Test Body");

        // Assert
        // Should return false since SMTP server isn't actually running
        Assert.False(result);
    }

    [Fact]
    public async Task SendHtmlEmailAsync_CallsSendEmailAsync() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        // Act
        var result = await service.SendHtmlEmailAsync(
            "test@example.com",
            "recipient@example.com",
            "Test Subject",
            "<html><body>Test HTML Body</body></html>");

        // Assert
        // Should return false since SMTP server isn't actually running
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithInvalidSmtpConfig_ReturnsFalse() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "nonexistent.invalid.domain",
            ["Smtp:Port"] = "587"
        };
        var service = CreateService(config);

        var request = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Test",
            Body = "Test body",
            IsHtml = false
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithMultipleRecipients_HandlesCorrectly() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        var request = new MessageRequest {
            ToAddress = "user1@example.com, user2@example.com, user3@example.com",
            Subject = "Test",
            Body = "Test body",
            IsHtml = false
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        // Should return false since SMTP server isn't running, but shouldn't throw
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithAttachments_HandlesCorrectly() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        var attachments = new List<MessageAttachmentRequest> {
            new() {
                Name = "test.txt",
                Content = System.Text.Encoding.UTF8.GetBytes("Test file content"),
                ContentType = "text/plain"
            }
        };

        var request = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Test with attachment",
            Body = "Test body",
            IsHtml = false,
            Attachments = attachments
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        // Should return false since SMTP server isn't running, but shouldn't throw
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithEmptyAttachmentsList_HandlesCorrectly() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        var request = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Test",
            Body = "Test body",
            IsHtml = false,
            Attachments = []
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithNullAttachments_HandlesCorrectly() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        var request = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Test",
            Body = "Test body",
            IsHtml = false,
            Attachments = null
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithHtmlContent_HandlesCorrectly() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "25"
        };
        var service = CreateService(config);

        var request = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "HTML Test",
            Body = "<html><body><h1>Test</h1><p>HTML content</p></body></html>",
            IsHtml = true
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WithCredentials_HandlesCorrectly() {
        // Arrange
        var config = new Dictionary<string, string?> {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "587",
            ["Smtp:Username"] = "testuser",
            ["Smtp:Password"] = "testpass",
            ["Smtp:EnableSsl"] = "true"
        };
        var service = CreateService(config);

        var request = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Test",
            Body = "Test body",
            IsHtml = false
        };

        // Act
        var result = await service.SendEmailAsync(request, "sender@example.com");

        // Assert
        Assert.False(result);
    }
}
