using Frimerki.Services.Common;
using Frimerki.Tests.Utilities;
using Xunit;

namespace Frimerki.Tests.Services;

public class NowProviderTests {
    [Fact]
    public void SystemNowProvider_ReturnsCurrentTime() {
        // Arrange
        var provider = new SystemNowProvider();
        var before = DateTime.UtcNow;

        // Act
        var result = provider.UtcNow;
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result >= before);
        Assert.True(result <= after);
    }

    [Fact]
    public void TestNowProvider_AllowsControllingTime() {
        // Arrange
        var testTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var provider = new TestNowProvider(testTime);

        // Act & Assert
        Assert.Equal(testTime, provider.UtcNow);
    }

    [Fact]
    public void TestNowProvider_AdvanceTimeSpan_UpdatesTime() {
        // Arrange
        var initialTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var provider = new TestNowProvider(initialTime);
        var timeToAdvance = TimeSpan.FromMinutes(45);

        // Act
        provider.Add(timeToAdvance);

        // Assert
        var expectedTime = initialTime.Add(timeToAdvance);
        Assert.Equal(expectedTime, provider.UtcNow);
    }

    [Fact]
    public void TestNowProvider_SetUtcNow_UpdatesTime() {
        // Arrange
        var initialTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var newTime = new DateTime(2024, 12, 25, 15, 45, 30, DateTimeKind.Utc);
        var provider = new TestNowProvider(initialTime);

        // Act
        provider.UtcNow = newTime;

        // Assert
        Assert.Equal(newTime, provider.UtcNow);
    }
}
