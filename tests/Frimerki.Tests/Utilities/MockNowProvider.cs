using Frimerki.Services.Common;

namespace Frimerki.Tests.Utilities;

/// <summary>
/// Test implementation of INowProvider that allows controlling the current time for testing.
/// </summary>
public class MockNowProvider : INowProvider {
    /// <summary>
    /// Initializes a new instance with the current UTC time.
    /// </summary>
    public MockNowProvider() : this(DateTime.UtcNow) { }

    /// <summary>
    /// Initializes a new instance with the specified time.
    /// </summary>
    /// <param name="utcNow">The UTC time to return.</param>
    public MockNowProvider(DateTime utcNow) => UtcNow = utcNow;

    /// <summary>
    /// Gets or sets the current UTC date and time.
    /// </summary>
    public DateTime UtcNow { get; set; }

    /// <summary>
    /// Advances the current time by the specified amount.
    /// </summary>
    /// <param name="timeSpan">The amount of time to advance.</param>
    public void Add(TimeSpan timeSpan) => UtcNow = UtcNow.Add(timeSpan);
}
