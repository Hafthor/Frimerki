namespace Frimerki.Models.Configuration;

public record AccountLockoutOptions(
    int MaxFailedAttempts = 5,
    int LockoutDurationMinutes = 15,
    int ResetWindowMinutes = 60,
    bool Enabled = true
) {
    public AccountLockoutOptions() : this(5) { } // why is this necessary?
}
