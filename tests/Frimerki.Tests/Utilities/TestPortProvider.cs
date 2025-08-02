namespace Frimerki.Tests.Utilities;

/// <summary>
/// Provides unique port numbers for integration tests to avoid port conflicts
/// </summary>
public static class TestPortProvider {
    private const int _startingPort = 19000; // Start from port 19000 to avoid common conflicts (9000 is used by php-fpm)
    private const int _portRange = 65536 - _startingPort;
    private static int _offsetPort = 0;

    /// <summary>
    /// Gets the next available test port number
    /// </summary>
    /// <returns>A unique port number for testing</returns>
    public static int GetNextPort() => GetPortRange();

    /// <summary>
    /// Gets a base port number and reserves a range of ports
    /// </summary>
    /// <param name="count">Number of ports to reserve</param>
    /// <returns>The base port number of the reserved range</returns>
    public static int GetPortRange(int count = 1) {
        return (Interlocked.Add(ref _offsetPort, count) - count) % _portRange + _startingPort;
    }
}
