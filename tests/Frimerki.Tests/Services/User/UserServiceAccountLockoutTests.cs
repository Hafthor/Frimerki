using System.Security.Cryptography;
using Frimerki.Data;
using Frimerki.Models.Configuration;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Common;
using Frimerki.Services.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Frimerki.Tests.Services.User;

public sealed class UserServiceAccountLockoutTests : IDisposable {
    private readonly EmailDbContext _context;
    private readonly UserService _userService;
    private readonly Mock<INowProvider> _mockNowProvider;
    private readonly Mock<ILogger<UserService>> _mockLogger;
    private readonly AccountLockoutOptions _lockoutOptions;
    private readonly DateTime _testTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public UserServiceAccountLockoutTests() {
        var options = new DbContextOptionsBuilder<EmailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new EmailDbContext(options);
        _mockNowProvider = new Mock<INowProvider>();
        _mockLogger = new Mock<ILogger<UserService>>();

        _lockoutOptions = new AccountLockoutOptions {
            Enabled = true,
            MaxFailedAttempts = 3,
            LockoutDurationMinutes = 15,
            ResetWindowMinutes = 60
        };

        var mockLockoutOptions = new Mock<IOptions<AccountLockoutOptions>>();
        mockLockoutOptions.Setup(x => x.Value).Returns(_lockoutOptions);

        _mockNowProvider.Setup(x => x.UtcNow).Returns(_testTime);

        _userService = new UserService(_context, _mockNowProvider.Object, _mockLogger.Object, mockLockoutOptions.Object);

        SeedTestData();
    }

    private void SeedTestData() {
        var domain = new DomainSettings {
            Id = 1,
            Name = "example.com",
            CreatedAt = _testTime
        };

        // Create test user with properly hashed password for "password123"
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var passwordHash = HashTestPassword("password123", salt);
        var user = new Frimerki.Models.Entities.User {
            Id = 1,
            Username = "testuser",
            DomainId = 1,
            PasswordHash = passwordHash,
            Salt = salt,
            FullName = "Test User",
            Role = "User",
            CanReceive = true,
            CanLogin = true,
            CreatedAt = _testTime,
            Domain = domain
        };

        _context.Domains.Add(domain);
        _context.Users.Add(user);
        _context.SaveChanges();
    }

    [Fact]
    public async Task AuthenticateUserEntityAsync_WithValidCredentials_ResetsFailedAttempts() {
        // Arrange
        const string email = "testuser@example.com";
        const string password = "password123";

        // Set up user with some failed attempts
        var user = await _context.Users.Include(u => u.Domain).FirstAsync();
        user.FailedLoginAttempts = 2;
        await _context.SaveChangesAsync();

        // Act
        var result = await _userService.AuthenticateUserEntityAsync(email, password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.FailedLoginAttempts);
        Assert.Null(result.LockoutEnd);
    }

    [Fact]
    public async Task AuthenticateUserEntityAsync_WithInvalidCredentials_IncrementsFailedAttempts() {
        // Arrange
        const string email = "testuser@example.com";
        const string password = "wrongpassword";

        // Act
        var result = await _userService.AuthenticateUserEntityAsync(email, password);

        // Assert
        Assert.Null(result);

        var user = await _context.Users.FirstAsync();
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Equal(_testTime, user.LastFailedLogin);
    }

    [Fact]
    public async Task AuthenticateUserEntityAsync_ReachingMaxAttempts_LocksAccount() {
        // Arrange
        const string email = "testuser@example.com";
        const string password = "wrongpassword";

        // Set user to have 2 failed attempts (one below max)
        var user = await _context.Users.FirstAsync();
        user.FailedLoginAttempts = 2;
        await _context.SaveChangesAsync();

        // Act - this should be the 3rd failed attempt, triggering lockout
        var result = await _userService.AuthenticateUserEntityAsync(email, password);

        // Assert
        Assert.Null(result);

        user = await _context.Users.FirstAsync();
        Assert.Equal(3, user.FailedLoginAttempts);
        Assert.NotNull(user.LockoutEnd);
        Assert.Equal(_testTime.AddMinutes(15), user.LockoutEnd);
    }

    [Fact]
    public async Task AuthenticateUserEntityAsync_AccountLocked_RejectsValidCredentials() {
        // Arrange
        const string email = "testuser@example.com";
        const string password = "password123";

        // Set up locked account
        var user = await _context.Users.FirstAsync();
        user.FailedLoginAttempts = 3;
        user.LockoutEnd = _testTime.AddMinutes(10); // Locked for 10 more minutes
        await _context.SaveChangesAsync();

        // Act
        var result = await _userService.AuthenticateUserEntityAsync(email, password);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateUserEntityAsync_LockoutExpired_AllowsLogin() {
        // Arrange
        const string email = "testuser@example.com";
        const string password = "password123";

        // Set up account that was locked but lockout has expired
        var user = await _context.Users.FirstAsync();
        user.FailedLoginAttempts = 3;
        user.LockoutEnd = _testTime.AddMinutes(-5); // Lockout expired 5 minutes ago
        await _context.SaveChangesAsync();

        // Act
        var result = await _userService.AuthenticateUserEntityAsync(email, password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.FailedLoginAttempts);
        Assert.Null(result.LockoutEnd);
    }

    [Fact]
    public async Task UpdateUserPasswordAsync_ResetsFailedAttempts() {
        // Arrange
        const string email = "testuser@example.com";
        var user = await _context.Users.FirstAsync();
        user.FailedLoginAttempts = 2;
        user.LockoutEnd = _testTime.AddMinutes(10);
        await _context.SaveChangesAsync();

        var request = new UserPasswordUpdateRequest {
            CurrentPassword = "password123",
            NewPassword = "newpassword123"
        };

        // Act
        var result = await _userService.UpdateUserPasswordAsync(email, request);

        // Assert
        Assert.True(result);

        user = await _context.Users.FirstAsync();
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEnd);
        Assert.Null(user.LastFailedLogin);
    }

    private string HashTestPassword(string password, string salt) {
        using Rfc2898DeriveBytes pbkdf2 = new(password, Convert.FromBase64String(salt), 10000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }

    public void Dispose() => _context.Dispose();
}
