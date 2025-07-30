namespace Frimerki.Services.Common;

/// <summary>
/// Test implementation of INowProvider that allows controlling the current time for testing.
/// </summary>
public class TestNowProvider : INowProvider {
    private DateTime _utcNow;

    /// <summary>
    /// Initializes a new instance with the current UTC time.
    /// </summary>
    public TestNowProvider() : this(DateTime.UtcNow) { }

    /// <summary>
    /// Initializes a new instance with the specified time.
    /// </summary>
    /// <param name="utcNow">The UTC time to return.</param>
    public TestNowProvider(DateTime utcNow) {
        _utcNow = utcNow;
    }

    /// <summary>
    /// Gets or sets the current UTC date and time.
    /// </summary>
    public DateTime UtcNow {
        get => _utcNow;
        set => _utcNow = value;
    }

    /// <summary>
    /// Advances the current time by the specified amount.
    /// </summary>
    /// <param name="timeSpan">The amount of time to advance.</param>
    public void Advance(TimeSpan timeSpan) {
        _utcNow = _utcNow.Add(timeSpan);
    }

    /// <summary>
    /// Advances the current time by the specified number of days.
    /// </summary>
    /// <param name="days">The number of days to advance.</param>
    public void AdvanceDays(int days) {
        _utcNow = _utcNow.AddDays(days);
    }

    /// <summary>
    /// Advances the current time by the specified number of hours.
    /// </summary>
    /// <param name="hours">The number of hours to advance.</param>
    public void AdvanceHours(int hours) {
        _utcNow = _utcNow.AddHours(hours);
    }

    /// <summary>
    /// Advances the current time by the specified number of minutes.
    /// </summary>
    /// <param name="minutes">The number of minutes to advance.</param>
    public void AdvanceMinutes(int minutes) {
        _utcNow = _utcNow.AddMinutes(minutes);
    }
}
