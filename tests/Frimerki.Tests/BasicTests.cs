using Xunit;

namespace Frimerki.Tests;

public class BasicTests {
    [Fact]
    public void Test_Basic_Addition() {
        // Arrange
        var a = 2;
        var b = 3;

        // Act
        var result = a + b;

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public void Test_Server_Name() {
        // Arrange
        var serverName = "Frímerki";

        // Act & Assert
        Assert.Equal("Frímerki", serverName);
        Assert.Contains("í", serverName); // Test that the accented character is preserved
    }
}
