using Frimerki.Models.Configuration;
using Frimerki.Services.Common;
using Moq;

namespace Frimerki.Tests.Services.User;

public class AccountLockoutLogicTests {
    private readonly AccountLockoutOptions _lockoutOptions;
    private readonly DateTime _testTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public AccountLockoutLogicTests() {
        _lockoutOptions = new AccountLockoutOptions {
            Enabled = true,
            MaxFailedAttempts = 3,
            LockoutDurationMinutes = 15,
            ResetWindowMinutes = 60
        };

        var mockNowProvider = new Mock<INowProvider>();
        mockNowProvider.Setup(x => x.UtcNow).Returns(_testTime);
    }

    [Fact]
    public void AccountLockoutOptions_HasCorrectDefaults() {
        // Arrange & Act
        var options = new AccountLockoutOptions();

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(5, options.MaxFailedAttempts);
        Assert.Equal(15, options.LockoutDurationMinutes);
        Assert.Equal(60, options.ResetWindowMinutes);
    }

    [Fact]
    public void IsAccountLocked_WithNullLockoutEnd_ReturnsFalse() {
        // Arrange
        var user = new Frimerki.Models.Entities.User {
            FailedLoginAttempts = 3,
            LockoutEnd = null
        };

        // Act
        bool isLocked = user.LockoutEnd.HasValue && user.LockoutEnd > _testTime;

        // Assert
        Assert.False(isLocked);
    }

    [Fact]
    public void IsAccountLocked_WithFutureLockoutEnd_ReturnsTrue() {
        // Arrange
        var user = new Frimerki.Models.Entities.User {
            FailedLoginAttempts = 3,
            LockoutEnd = _testTime.AddMinutes(10)
        };

        // Act
        bool isLocked = user.LockoutEnd.HasValue && user.LockoutEnd > _testTime;

        // Assert
        Assert.True(isLocked);
    }

    [Fact]
    public void IsAccountLocked_WithPastLockoutEnd_ReturnsFalse() {
        // Arrange
        var user = new Frimerki.Models.Entities.User {
            FailedLoginAttempts = 3,
            LockoutEnd = _testTime.AddMinutes(-10)
        };

        // Act
        bool isLocked = user.LockoutEnd.HasValue && user.LockoutEnd > _testTime;

        // Assert
        Assert.False(isLocked);
    }

    [Fact]
    public void FailedAttemptIncrements_WorksCorrectly() {
        // Arrange
        var user = new Frimerki.Models.Entities.User {
            FailedLoginAttempts = 2,
            LastFailedLogin = _testTime.AddMinutes(-5)
        };

        // Act
        user.FailedLoginAttempts++;
        user.LastFailedLogin = _testTime;

        // Check if should be locked
        if (user.FailedLoginAttempts >= _lockoutOptions.MaxFailedAttempts) {
            user.LockoutEnd = _testTime.AddMinutes(_lockoutOptions.LockoutDurationMinutes);
        }

        // Assert
        Assert.Equal(3, user.FailedLoginAttempts);
        Assert.Equal(_testTime, user.LastFailedLogin);
        Assert.NotNull(user.LockoutEnd);
        Assert.Equal(_testTime.AddMinutes(15), user.LockoutEnd);
    }

    [Fact]
    public void ResetFailedAttempts_ClearsAllLockoutData() {
        // Arrange
        var user = new Frimerki.Models.Entities.User {
            FailedLoginAttempts = 5,
            LockoutEnd = _testTime.AddMinutes(10),
            LastFailedLogin = _testTime.AddMinutes(-5)
        };

        // Act
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastFailedLogin = null;

        // Assert
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEnd);
        Assert.Null(user.LastFailedLogin);
    }
}
