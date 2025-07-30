namespace Frimerki.Services.Common;

/// <summary>
/// Provides the current date and time for dependency injection and testability.
/// </summary>
public interface INowProvider {
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTime UtcNow { get; }
}
