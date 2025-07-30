namespace Frimerki.Services.Common;

/// <summary>
/// Default implementation of INowProvider that returns the actual current time.
/// </summary>
public class SystemNowProvider : INowProvider {
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;
}
