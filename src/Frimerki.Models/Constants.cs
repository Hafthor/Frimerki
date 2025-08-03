namespace Frimerki.Models;

public static class Constants {
    private const string ValidDomainRegexPattern =
        @"[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*";
    private const string ValidUsernameRegexPattern = "[a-zA-Z0-9._-]+";

    public const string ValidDomainRegex = $"^{ValidDomainRegexPattern}$";
    public const string ValidDkimRegex = @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$";
    public const string ValidUsernameRegex = $"^{ValidUsernameRegexPattern}$";
    public const string ValidUserRoleRegex = "^(User|DomainAdmin|HostAdmin)$";
    public const string ValidEmailRegex = $"({ValidUsernameRegexPattern})@({ValidDomainRegexPattern})";
}
