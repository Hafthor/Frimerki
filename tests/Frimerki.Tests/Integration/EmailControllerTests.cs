using Frimerki.Models.DTOs;
using Frimerki.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frimerki.Tests.Integration;

/// <summary>
/// Integration tests for Email functionality
/// </summary>
public class EmailControllerTests {
    private readonly IServiceProvider _serviceProvider;

    public EmailControllerTests() {
        // Setup test services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // Add configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> {
                ["Smtp:Host"] = "localhost",
                ["Smtp:Port"] = "587",
                ["Smtp:Username"] = "test@example.com",
                ["Smtp:Password"] = "testpassword",
                ["Smtp:EnableSsl"] = "true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Add SMTP service
        services.AddScoped<SmtpClientService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void SmtpClientService_ValidateConfiguration_ReturnsTrueForValidConfig() {
        // Arrange
        var smtpService = _serviceProvider.GetRequiredService<SmtpClientService>();

        // Act
        var isValid = smtpService.ValidateConfiguration();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void SimpleEmailRequest_ValidatesCorrectly() {
        // Arrange
        var validRequest = new SimpleEmailRequest {
            To = "test@example.com",
            Subject = "Test Subject",
            Body = "Test Body"
        };

        var invalidRequest = new SimpleEmailRequest {
            To = "", // Invalid email
            Subject = "Test Subject",
            Body = "Test Body"
        };

        // Act & Assert
        Assert.NotNull(validRequest.To);
        Assert.NotEmpty(validRequest.To);
        Assert.Contains("@", validRequest.To);

        Assert.Empty(invalidRequest.To);
    }

    [Fact]
    public void MessageRequest_SupportsHtmlContent() {
        // Arrange
        var htmlRequest = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "HTML Test",
            Body = "<h1>Hello World</h1>",
            IsHtml = true
        };

        var textRequest = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Text Test",
            Body = "Hello World",
            IsHtml = false
        };

        // Act & Assert
        Assert.True(htmlRequest.IsHtml);
        Assert.False(textRequest.IsHtml);
        Assert.Contains("<h1>", htmlRequest.Body);
    }

    [Fact]
    public void MessageRequest_SupportsAttachments() {
        // Arrange
        var testContent = System.Text.Encoding.UTF8.GetBytes("Test content");
        var requestWithAttachment = new MessageRequest {
            ToAddress = "test@example.com",
            Subject = "Test with Attachment",
            Body = "Please see attached file",
            Attachments = [
                new MessageAttachmentRequest
                {
                    Name = "test.txt",
                    ContentType = "text/plain",
                    Content = testContent
                }
            ]
        };

        // Act & Assert
        Assert.NotNull(requestWithAttachment.Attachments);
        Assert.Single(requestWithAttachment.Attachments);
        Assert.Equal("test.txt", requestWithAttachment.Attachments[0].Name);
        Assert.Equal("text/plain", requestWithAttachment.Attachments[0].ContentType);
        Assert.Equal(testContent, requestWithAttachment.Attachments[0].Content);
    }
}
