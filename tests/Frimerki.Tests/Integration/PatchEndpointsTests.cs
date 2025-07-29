using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Frimerki.Data;
using Frimerki.Models.Entities;
using Frimerki.Models.DTOs;
using Frimerki.Models.DTOs.Folder;
using Frimerki.Services.User;

namespace Frimerki.Tests.Integration;

public class PatchEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _databaseName;

    public PatchEndpointsTests(WebApplicationFactory<Program> factory) {
        _databaseName = "TestDatabase_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EmailDbContext>));
                if (descriptor != null) {
                    services.Remove(descriptor);
                }

                services.AddDbContext<EmailDbContext>(options => {
                    options.UseInMemoryDatabase(_databaseName);
                });
            });
        });

        _client = _factory.CreateClient();

        SeedTestData();
    }

    private void SeedTestData() {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EmailDbContext>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        var domain = new Domain {
            Id = 1,
            Name = "example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.Domains.Add(domain);
        context.SaveChanges();

        // Create test user with proper password hashing
        var userRequest = new CreateUserRequest {
            Username = "testuser",
            DomainName = "example.com",
            Password = "password123",
            FullName = "Test User"
        };
        userService.CreateUserAsync(userRequest).Wait();
    }

    private async Task<string> GetAuthTokenAsync() {
        var loginRequest = new LoginRequest {
            Email = "testuser@example.com",
            Password = "password123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginResult = JsonSerializer.Deserialize<LoginResponse>(loginContent, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        return loginResult!.Token;
    }

    [Fact]
    public async Task PatchDomain_WithValidToken_ReturnsSuccess() {
        // Arrange
        var token = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // First verify the domain exists
        var getResponse = await _client.GetAsync("/api/domains/example.com");
        var getContent = await getResponse.Content.ReadAsStringAsync();

        Assert.True(getResponse.IsSuccessStatusCode, $"Domain not found for GET request. Status: {getResponse.StatusCode}, Content: {getContent}");

        var patchRequest = new DomainUpdateRequest {
            Name = "newexample.com"
        };

        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/domains/example.com") {
            Content = JsonContent.Create(patchRequest)
        });

        // Assert
        if (!response.IsSuccessStatusCode) {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"PATCH failed. Status: {response.StatusCode}, Content: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<DomainResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("newexample.com", result.Name);
    }

    [Fact]
    public async Task PatchFolder_WithValidToken_ReturnsSuccess() {
        // Arrange
        var token = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // First create a folder
        var createRequest = new FolderRequest {
            Name = "TestFolder"
        };
        await _client.PostAsJsonAsync("/api/folders", createRequest);

        var patchRequest = new FolderUpdateRequest {
            Name = "RenamedFolder"
        };

        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/folders/TestFolder") {
            Content = JsonContent.Create(patchRequest)
        });

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FolderResponse>(content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("RenamedFolder", result.Name);
    }

    public void Dispose() {
        _client?.Dispose();
    }
}
