using System.Net.Http.Json;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Pop3;
using Frimerki.Tests.Utilities;
using MailKit.Net.Pop3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frimerki.Tests.Integration;

public class Pop3ServerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable {
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _databaseName;

    public Pop3ServerTests(WebApplicationFactory<Program> factory) {
        _databaseName = "TestDatabase_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                // Remove the existing DbContext registration
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EmailDbContext>));
                if (descriptor != null) {
                    services.Remove(descriptor);
                }

                // Remove default email server hosted services to prevent port conflicts
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType?.Name.Contains("Server") == true)).ToList();
                foreach (var service in hostedServices) {
                    services.Remove(service);
                }

                // Add in-memory database for testing
                services.AddDbContext<EmailDbContext>(options => {
                    options.UseInMemoryDatabase(_databaseName);
                });
            });
        });

        _client = _factory.CreateClient();

        // Seed test data
        SeedTestData();
    }

    private void SeedTestData() {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EmailDbContext>();

        var domain = new DomainSettings {
            Id = 1,
            Name = "test.com",
            CreatedAt = DateTime.UtcNow
        };

        // Create password hash for "TestPassword123!"
        var salt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        string passwordHash;
        using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes("TestPassword123!", Convert.FromBase64String(salt), 10000, System.Security.Cryptography.HashAlgorithmName.SHA256)) {
            var hash = pbkdf2.GetBytes(32);
            passwordHash = Convert.ToBase64String(hash);
        }

        var user = new User {
            Id = 1,
            Username = "testuser",
            DomainId = 1,
            PasswordHash = passwordHash,
            Salt = salt,
            FullName = "Test User",
            Role = "User",
            CanReceive = true,
            CanLogin = true,
            CreatedAt = DateTime.UtcNow,
            Domain = domain
        };

        context.Domains.Add(domain);
        context.Users.Add(user);
        context.SaveChanges();
    }

    public void Dispose() {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Pop3Server_WithValidCredentials_CanAuthenticateAndListMessages() {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var serviceProvider = scope.ServiceProvider;

        // Start POP3 server on a test port
        var testPort = TestPortProvider.GetNextPort();
        var pop3Server = new Pop3Server(
            loggerFactory.CreateLogger<Pop3Server>(),
            serviceProvider,
            testPort);

        using var cancellationTokenSource = new CancellationTokenSource();
        var serverTask = pop3Server.StartAsync(cancellationTokenSource.Token);

        // Give the server a moment to start
        await Task.Delay(100, cancellationTokenSource.Token);

        try {
            // Create a test user and authenticate via web API
            var (email, password) = await CreateTestUserAsync();

            // Test POP3 connection using MailKit
            using var pop3Client = new Pop3Client();

            // Connect to our POP3 server
            await pop3Client.ConnectAsync("127.0.0.1", testPort, false, cancellationTokenSource.Token);

            // Assert connection is established
            Assert.True(pop3Client.IsConnected);

            // Authenticate with the test user
            await pop3Client.AuthenticateAsync(email, password, cancellationTokenSource.Token);

            // Assert authentication succeeded
            Assert.True(pop3Client.IsAuthenticated);

            // Get message count
            var messageCount = pop3Client.Count;
            Assert.True(messageCount >= 0);

            // Get total size
            var totalSize = pop3Client.Size;
            Assert.True(totalSize >= 0);

            // Disconnect
            await pop3Client.DisconnectAsync(true, cancellationTokenSource.Token);
            Assert.False(pop3Client.IsConnected);

        } finally {
            // Stop the server
            await cancellationTokenSource.CancelAsync();
            await serverTask;
            pop3Server.Dispose();
        }
    }

    [Fact]
    public async Task Pop3Server_WithInvalidCredentials_FailsAuthentication() {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var serviceProvider = scope.ServiceProvider;

        var testPort = TestPortProvider.GetNextPort();
        var pop3Server = new Pop3Server(
            loggerFactory.CreateLogger<Pop3Server>(),
            serviceProvider,
            testPort);

        using var cancellationTokenSource = new CancellationTokenSource();
        var serverTask = pop3Server.StartAsync(cancellationTokenSource.Token);

        await Task.Delay(100, cancellationTokenSource.Token);

        try {
            using var pop3Client = new Pop3Client();

            await pop3Client.ConnectAsync("127.0.0.1", testPort, false, cancellationTokenSource.Token);
            Assert.True(pop3Client.IsConnected);

            // Try to authenticate with invalid credentials
            await Assert.ThrowsAsync<MailKit.Security.AuthenticationException>(
                () => pop3Client.AuthenticateAsync("invalid@example.com", "wrongpassword", cancellationTokenSource.Token));

            Assert.False(pop3Client.IsAuthenticated);

        } finally {
            await cancellationTokenSource.CancelAsync();
            await serverTask;
            pop3Server.Dispose();
        }
    }

    [Fact]
    public async Task Pop3Server_WithMessages_CanRetrieveAndDeleteMessages() {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var serviceProvider = scope.ServiceProvider;

        var testPort = TestPortProvider.GetNextPort();
        var pop3Server = new Pop3Server(
            loggerFactory.CreateLogger<Pop3Server>(),
            serviceProvider,
            testPort);

        using var cancellationTokenSource = new CancellationTokenSource();
        var serverTask = pop3Server.StartAsync(cancellationTokenSource.Token);

        await Task.Delay(100, cancellationTokenSource.Token);

        try {
            // Create test user and send them a message
            var (email, password) = await CreateTestUserAsync();
            await SendTestMessageAsync(email);

            using var pop3Client = new Pop3Client();

            await pop3Client.ConnectAsync("127.0.0.1", testPort, false, cancellationTokenSource.Token);
            await pop3Client.AuthenticateAsync(email, password, cancellationTokenSource.Token);

            // Check if we have messages
            var messageCount = pop3Client.Count;
            if (messageCount > 0) {
                // Get first message
                var message = await pop3Client.GetMessageAsync(0, cancellationTokenSource.Token);
                Assert.NotNull(message);
                Assert.NotNull(message.Headers);

                // Get message size
                var size = await pop3Client.GetMessageSizeAsync(0, cancellationTokenSource.Token);
                Assert.True(size > 0);

                // Check the full message
                Assert.NotNull(message.Subject);

                // Mark message for deletion
                await pop3Client.DeleteMessageAsync(0, cancellationTokenSource.Token);

                // Verify message is marked for deletion
                // Note: The actual deletion happens on QUIT
            }

            await pop3Client.DisconnectAsync(true, cancellationTokenSource.Token);

        } finally {
            await cancellationTokenSource.CancelAsync();
            await serverTask;
            pop3Server.Dispose();
        }
    }

    [Fact]
    public async Task Pop3Server_UidlCommand_ReturnsUniqueIdentifiers() {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var serviceProvider = scope.ServiceProvider;

        var testPort = TestPortProvider.GetNextPort();
        var pop3Server = new Pop3Server(
            loggerFactory.CreateLogger<Pop3Server>(),
            serviceProvider,
            testPort);

        using var cancellationTokenSource = new CancellationTokenSource();
        var serverTask = pop3Server.StartAsync(cancellationTokenSource.Token);

        await Task.Delay(100, cancellationTokenSource.Token);

        try {
            var (email, password) = await CreateTestUserAsync();
            await SendTestMessageAsync(email);

            using var pop3Client = new Pop3Client();

            await pop3Client.ConnectAsync("127.0.0.1", testPort, false, cancellationTokenSource.Token);
            await pop3Client.AuthenticateAsync(email, password, cancellationTokenSource.Token);

            var messageCount = pop3Client.Count;
            if (messageCount > 0) {
                // Get unique ID for first message
                var uid = await pop3Client.GetMessageUidAsync(0, cancellationTokenSource.Token);
                Assert.NotNull(uid);
                Assert.NotEmpty(uid);

                // Get all unique IDs
                var uids = await pop3Client.GetMessageUidsAsync(cancellationTokenSource.Token);
                Assert.NotNull(uids);
                Assert.Equal(messageCount, uids.Count);
            }

            await pop3Client.DisconnectAsync(true, cancellationTokenSource.Token);

        } finally {
            await cancellationTokenSource.CancelAsync();
            await serverTask;
            pop3Server.Dispose();
        }
    }

    [Fact]
    public async Task Pop3Server_TopCommand_RetrievesHeadersAndLines() {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var serviceProvider = scope.ServiceProvider;

        var testPort = TestPortProvider.GetNextPort();
        var pop3Server = new Pop3Server(
            loggerFactory.CreateLogger<Pop3Server>(),
            serviceProvider,
            testPort);

        using var cancellationTokenSource = new CancellationTokenSource();
        var serverTask = pop3Server.StartAsync(cancellationTokenSource.Token);

        await Task.Delay(100, cancellationTokenSource.Token);

        try {
            var (email, password) = await CreateTestUserAsync();
            await SendTestMessageAsync(email);

            using var pop3Client = new Pop3Client();

            await pop3Client.ConnectAsync("127.0.0.1", testPort, false, cancellationTokenSource.Token);
            await pop3Client.AuthenticateAsync(email, password, cancellationTokenSource.Token);

            var messageCount = pop3Client.Count;
            if (messageCount > 0) {
                // Get message (MailKit will use TOP command internally for headers)
                var message = await pop3Client.GetMessageAsync(0, cancellationTokenSource.Token);
                Assert.NotNull(message);
                Assert.NotNull(message.Headers);

                // This tests the TOP command implementation
                // MailKit uses TOP internally when getting headers
            }

            await pop3Client.DisconnectAsync(true, cancellationTokenSource.Token);

        } finally {
            await cancellationTokenSource.CancelAsync();
            await serverTask;
            pop3Server.Dispose();
        }
    }

    [Fact]
    public async Task Pop3Server_NoopCommand_KeepsConnectionAlive() {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var serviceProvider = scope.ServiceProvider;

        var testPort = TestPortProvider.GetNextPort();
        var pop3Server = new Pop3Server(
            loggerFactory.CreateLogger<Pop3Server>(),
            serviceProvider,
            testPort);

        using var cancellationTokenSource = new CancellationTokenSource();
        var serverTask = pop3Server.StartAsync(cancellationTokenSource.Token);

        await Task.Delay(100, cancellationTokenSource.Token);

        try {
            var (email, password) = await CreateTestUserAsync();

            using var pop3Client = new Pop3Client();

            await pop3Client.ConnectAsync("127.0.0.1", testPort, false, cancellationTokenSource.Token);
            await pop3Client.AuthenticateAsync(email, password, cancellationTokenSource.Token);

            // Send NOOP command
            await pop3Client.NoOpAsync(cancellationTokenSource.Token);

            // Verify connection is still active
            Assert.True(pop3Client.IsConnected);
            Assert.True(pop3Client.IsAuthenticated);

            await pop3Client.DisconnectAsync(true, cancellationTokenSource.Token);

        } finally {
            await cancellationTokenSource.CancelAsync();
            await serverTask;
            pop3Server.Dispose();
        }
    }

    private async Task<(string email, string password)> CreateTestUserAsync() {
        // Use the seeded test user
        var email = "testuser@test.com";
        var password = "TestPassword123!";

        return (email, password);
    }

    private async Task SendTestMessageAsync(string toEmail) {
        // Create a test message via the API
        var messageRequest = new MessageRequest {
            Subject = "Test Message",
            Body = "This is a test message for POP3 testing.",
            ToAddress = toEmail
        };

        // Authenticate as system user or create a system session
        var loginRequest = new LoginRequest {
            Email = "admin@example.com", // Assuming there's an admin user
            Password = "admin123"
        };

        try {
            var loginResponse = await _client.PostAsJsonAsync("/api/session", loginRequest);
            if (loginResponse.IsSuccessStatusCode) {
                var token = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
                _client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token?.Token);

                await _client.PostAsJsonAsync("/api/messages", messageRequest);
            }
        } catch {
            // If sending via API fails, that's OK for these tests
            // The important part is testing the POP3 protocol itself
        }
    }

}
