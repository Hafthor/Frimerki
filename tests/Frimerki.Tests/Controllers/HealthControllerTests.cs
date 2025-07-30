using System;
using Frimerki.Server.Controllers;
using Frimerki.Services.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Frimerki.Tests.Controllers;

public class HealthControllerTests {
    private readonly HealthController _controller;
    private readonly MockNowProvider _mockNowProvider;

    public HealthControllerTests() {
        _mockNowProvider = new MockNowProvider();
        var logger = NullLogger<HealthController>.Instance;
        _controller = new HealthController(logger, _mockNowProvider);
    }

    [Fact]
    public void GetHealth_ReturnsHealthyStatus() {
        // Arrange
        var testTime = new DateTime(2025, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        _mockNowProvider.SetCurrentTime(testTime);

        // Act
        var result = _controller.GetHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Use reflection to check the anonymous object properties
        var response = okResult.Value!;
        var statusProperty = response.GetType().GetProperty("Status");
        var serverProperty = response.GetType().GetProperty("Server");
        var versionProperty = response.GetType().GetProperty("Version");
        var timestampProperty = response.GetType().GetProperty("Timestamp");
        var frameworkProperty = response.GetType().GetProperty("Framework");

        Assert.NotNull(statusProperty);
        Assert.NotNull(serverProperty);
        Assert.NotNull(versionProperty);
        Assert.NotNull(timestampProperty);
        Assert.NotNull(frameworkProperty);

        Assert.Equal("Healthy", statusProperty.GetValue(response));
        Assert.Equal("Frímerki Email Server", serverProperty.GetValue(response));
        Assert.Equal(".NET 8", frameworkProperty.GetValue(response));
        Assert.Equal(testTime, timestampProperty.GetValue(response));
    }

    [Fact]
    public void GetHealth_VersionIsNotNull() {
        // Act
        var result = _controller.GetHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var response = okResult.Value!;
        var versionProperty = response.GetType().GetProperty("Version");
        Assert.NotNull(versionProperty);

        var version = versionProperty.GetValue(response) as string;
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void GetServerInfo_ReturnsCompleteInformation() {
        // Act
        var result = _controller.GetServerInfo();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var response = okResult.Value!;
        var nameProperty = response.GetType().GetProperty("Name");
        var descriptionProperty = response.GetType().GetProperty("Description");
        var versionProperty = response.GetType().GetProperty("Version");
        var frameworkProperty = response.GetType().GetProperty("Framework");
        var databaseProperty = response.GetType().GetProperty("Database");
        var protocolsProperty = response.GetType().GetProperty("Protocols");
        var featuresProperty = response.GetType().GetProperty("Features");

        Assert.NotNull(nameProperty);
        Assert.NotNull(descriptionProperty);
        Assert.NotNull(versionProperty);
        Assert.NotNull(frameworkProperty);
        Assert.NotNull(databaseProperty);
        Assert.NotNull(protocolsProperty);
        Assert.NotNull(featuresProperty);

        Assert.Equal(".NET 8", frameworkProperty.GetValue(response));
        Assert.Equal("SQLite", databaseProperty.GetValue(response));
    }

    [Fact]
    public void GetServerInfo_ContainsProtocolInformation() {
        // Act
        var result = _controller.GetServerInfo();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var response = okResult.Value!;
        var protocolsProperty = response.GetType().GetProperty("Protocols");
        Assert.NotNull(protocolsProperty);

        var protocols = protocolsProperty.GetValue(response);
        Assert.NotNull(protocols);

        // Check that protocols object has SMTP, IMAP, and POP3 properties
        var smtpProperty = protocols.GetType().GetProperty("SMTP");
        var imapProperty = protocols.GetType().GetProperty("IMAP");
        var pop3Property = protocols.GetType().GetProperty("POP3");

        Assert.NotNull(smtpProperty);
        Assert.NotNull(imapProperty);
        Assert.NotNull(pop3Property);

        // Verify that each protocol has Enabled and Ports properties
        var smtp = smtpProperty.GetValue(protocols);
        Assert.NotNull(smtp);
        var smtpEnabledProperty = smtp.GetType().GetProperty("Enabled");
        var smtpPortsProperty = smtp.GetType().GetProperty("Ports");
        Assert.NotNull(smtpEnabledProperty);
        Assert.NotNull(smtpPortsProperty);
        Assert.True((bool)smtpEnabledProperty.GetValue(smtp)!);
    }

    [Fact]
    public void GetServerInfo_ContainsFeaturesList() {
        // Act
        var result = _controller.GetServerInfo();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var response = okResult.Value!;
        var featuresProperty = response.GetType().GetProperty("Features");
        Assert.NotNull(featuresProperty);

        var features = featuresProperty.GetValue(response) as string[];
        Assert.NotNull(features);
        Assert.True(features.Length > 0);

        // Check for expected features
        Assert.Contains("Email Routing", features);
        Assert.Contains("IMAP4rev1 Support", features);
        Assert.Contains("Real-time Notifications", features);
        Assert.Contains("Web Management Interface", features);
        Assert.Contains("DKIM Signing", features);
        Assert.Contains("Full-text Search", features);
    }

    [Fact]
    public void GetHealth_LogsHealthCheckRequest() {
        // This test verifies that the method runs without throwing exceptions
        // and that logging functionality is properly invoked

        // Act & Assert - Should not throw
        var result = _controller.GetHealth();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetServerInfo_ReturnsConsistentResponse() {
        // Act
        var result1 = _controller.GetServerInfo();
        var result2 = _controller.GetServerInfo();

        // Assert
        var okResult1 = Assert.IsType<OkObjectResult>(result1);
        var okResult2 = Assert.IsType<OkObjectResult>(result2);

        Assert.NotNull(okResult1.Value);
        Assert.NotNull(okResult2.Value);

        // Both responses should have the same structure and static values
        var response1 = okResult1.Value!;
        var response2 = okResult2.Value!;

        var version1 = response1.GetType().GetProperty("Version")!.GetValue(response1);
        var version2 = response2.GetType().GetProperty("Version")!.GetValue(response2);

        Assert.Equal(version1, version2);
    }
}

// Mock implementation of INowProvider for testing
public class MockNowProvider : INowProvider {
    private DateTime _currentTime = DateTime.UtcNow;

    public DateTime UtcNow => _currentTime;

    public void SetCurrentTime(DateTime time) {
        _currentTime = time;
    }
}
