namespace Frimerki.Models.Configuration;

public class AccountLockoutOptions {
    /// <summary>
    /// Number of failed login attempts before account is locked
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Duration of account lockout in minutes
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Time window in minutes to reset failed attempt counter
    /// </summary>
    public int ResetWindowMinutes { get; set; } = 60;

    /// <summary>
    /// Whether account lockout is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}
