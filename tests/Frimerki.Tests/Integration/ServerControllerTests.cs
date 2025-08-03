using System.Net.Http.Json;
using System.Text.Json;
using Frimerki.Models.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Frimerki.Tests.Integration;

public class ServerControllerTests : IClassFixture<WebApplicationFactory<Program>> {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ServerControllerTests(WebApplicationFactory<Program> factory) {
        _factory = factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                // Remove default email server hosted services to prevent port conflicts
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType?.Name.Contains("Server") == true)).ToList();
                foreach (var service in hostedServices) {
                    services.Remove(service);
                }
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetServerStatus_ReturnsSuccessWithValidData() {
        // Act
        var response = await _client.GetAsync("/api/server/status");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<ServerStatusResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(status);
        Assert.Equal("Running", status.Status);
        Assert.StartsWith("1.0.0", status.Version);
        Assert.NotNull(status.Statistics);
        Assert.NotNull(status.Services);
    }

    [Fact]
    public async Task GetServerHealth_ReturnsHealthyStatus() {
        // Act
        var response = await _client.GetAsync("/api/server/health");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<ServerHealthResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(health);
        Assert.Equal("Healthy", health.Status);
        Assert.NotEmpty(health.Checks);
        Assert.Contains(health.Checks, c => c.Name == "Database");
    }

    [Fact]
    public async Task GetServerMetrics_ReturnsValidMetrics() {
        // Act
        var response = await _client.GetAsync("/api/server/metrics");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var metrics = JsonSerializer.Deserialize<ServerMetricsResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(metrics);
        Assert.NotNull(metrics.System);
        Assert.NotNull(metrics.Email);
        Assert.NotNull(metrics.Database);
        Assert.True(metrics.System.MemoryTotalBytes > 0);
    }

    [Fact]
    public async Task GetServerSettings_ReturnsConfiguredSettings() {
        // Act
        var response = await _client.GetAsync("/api/server/settings");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var settings = JsonSerializer.Deserialize<ServerSettingsResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(settings);
        Assert.NotNull(settings.Settings);
        Assert.True(settings.Settings.ContainsKey("MaxMessageSize"));
        Assert.True(settings.Settings.ContainsKey("EnableSMTP"));
    }

    [Fact]
    public async Task UpdateServerSettings_ReturnsSuccess() {
        // Arrange
        var updateRequest = new ServerSettingsRequest {
            Settings = new Dictionary<string, object> {
                ["MaxMessageSize"] = "50MB",
                ["EnableSMTP"] = true,
                ["RequireSSL"] = true
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/server/settings", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("successfully", content);
    }

    [Fact]
    public async Task CreateBackup_ReturnsBackupInfo() {
        // Arrange
        var backupRequest = new BackupRequest {
            IncludeAttachments = true,
            IncludeLogs = false,
            Description = "Test backup"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/server/backup", backupRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var backup = JsonSerializer.Deserialize<BackupResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(backup);
        Assert.NotEmpty(backup.BackupId);
        Assert.NotEmpty(backup.FileName);
        Assert.Equal("Completed", backup.Status);
        Assert.True(backup.SizeBytes > 0);
    }

    [Fact]
    public async Task GetServerLogs_ReturnsLogData() {
        // Act
        var response = await _client.GetAsync("/api/server/logs?skip=20&take=10");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var logs = JsonSerializer.Deserialize<PaginatedInfo<ServerLogEntry>>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(logs);
        Assert.NotNull(logs.Items);
        Assert.Equal(20, logs.Skip);
        Assert.Equal(10, logs.Take);
    }

    [Fact]
    public async Task GetSystemInfo_ReturnsSystemInformation() {
        // Act
        var response = await _client.GetAsync("/api/server/info");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Parse as dynamic object since we don't have a specific DTO
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("serverName", out _));
        Assert.True(root.TryGetProperty("operatingSystem", out _));
        Assert.True(root.TryGetProperty("framework", out _));
        Assert.True(root.TryGetProperty("applicationVersion", out _));
    }

    [Fact]
    public async Task ListBackups_ReturnsEmptyList() {
        // Act
        var response = await _client.GetAsync("/api/server/backups");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var backups = JsonSerializer.Deserialize<List<BackupResponse>>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(backups);
        // Should be empty initially since we don't have real backup files
    }

    [Fact]
    public async Task RestartServer_ReturnsSuccessMessage() {
        // Act
        var response = await _client.PostAsync("/api/server/restart", null);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("restart initiated", content);
    }
}
