using Frimerki.Data;
using Frimerki.Models.Configuration;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.User;
using Frimerki.Tests.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Frimerki.Tests.Services;

public class UserServiceTimeTests {
    private readonly EmailDbContext _context;
    private readonly MockNowProvider _nowProvider;
    private readonly UserService _userService;

    public UserServiceTimeTests() {
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new EmailDbContext(options);
        var mockLogger = new Mock<ILogger<UserService>>();
        _nowProvider = new MockNowProvider();

        var lockoutOptions = new AccountLockoutOptions();
        var mockLockoutOptions = new Mock<IOptions<AccountLockoutOptions>>();
        mockLockoutOptions.Setup(x => x.Value).Returns(lockoutOptions);

        _userService = new UserService(_context, _nowProvider, mockLogger.Object, mockLockoutOptions.Object);

        SeedTestData();
    }

    private void SeedTestData() {
        var domain = new DomainSettings {
            Id = 1,
            Name = "example.com",
            CreatedAt = _nowProvider.UtcNow
        };

        _context.Domains.Add(domain);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CreateUser_UsesNowProviderForCreatedAt() {
        // Arrange
        var testTime = new DateTime(2024, 3, 15, 14, 30, 0, DateTimeKind.Utc);
        _nowProvider.UtcNow = testTime;

        var request = new CreateUserRequest {
            Username = "testuser",
            DomainName = "example.com",
            Password = "securepassword123",
            FullName = "Test User",
            Role = "User",
            CanReceive = true,
            CanLogin = true
        };

        // Act
        var result = await _userService.CreateUserAsync(request);

        // Assert
        Assert.NotNull(result);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        Assert.NotNull(user);
        Assert.Equal(testTime, user.CreatedAt);
    }

    [Fact]
    public async Task AuthenticateUser_UsesNowProviderForLastLogin() {
        // Arrange
        var createTime = new DateTime(2024, 3, 15, 14, 30, 0, DateTimeKind.Utc);
        var loginTime = new DateTime(2024, 3, 16, 10, 15, 0, DateTimeKind.Utc);

        // Create user at specific time
        _nowProvider.UtcNow = createTime;
        var createRequest = new CreateUserRequest {
            Username = "testuser2",
            DomainName = "example.com",
            Password = "securepassword123",
            FullName = "Test User 2",
            Role = "User",
            CanReceive = true,
            CanLogin = true
        };
        await _userService.CreateUserAsync(createRequest);

        // Set time for login
        _nowProvider.UtcNow = loginTime;

        // Act
        var result = await _userService.AuthenticateUserAsync("testuser2@example.com", "securepassword123");

        // Assert
        Assert.NotNull(result);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == "testuser2");
        Assert.NotNull(user);
        Assert.Equal(createTime, user.CreatedAt);
        Assert.Equal(loginTime, user.LastLogin);
    }

    [Fact]
    public async Task MultipleLogins_TracksLastLoginCorrectly() {
        // Arrange
        var createTime = new DateTime(2024, 3, 15, 14, 30, 0, DateTimeKind.Utc);
        var firstLoginTime = new DateTime(2024, 3, 16, 10, 15, 0, DateTimeKind.Utc);
        var secondLoginTime = new DateTime(2024, 3, 17, 15, 45, 0, DateTimeKind.Utc);

        // Create user
        _nowProvider.UtcNow = createTime;
        var createRequest = new CreateUserRequest {
            Username = "testuser3",
            DomainName = "example.com",
            Password = "securepassword123",
            FullName = "Test User 3",
            Role = "User",
            CanReceive = true,
            CanLogin = true
        };
        await _userService.CreateUserAsync(createRequest);

        // First login
        _nowProvider.UtcNow = firstLoginTime;
        await _userService.AuthenticateUserAsync("testuser3@example.com", "securepassword123");

        // Second login
        _nowProvider.UtcNow = secondLoginTime;
        await _userService.AuthenticateUserAsync("testuser3@example.com", "securepassword123");

        // Assert
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == "testuser3");
        Assert.NotNull(user);
        Assert.Equal(createTime, user.CreatedAt);
        Assert.Equal(secondLoginTime, user.LastLogin); // Should be the latest login time
    }
}
