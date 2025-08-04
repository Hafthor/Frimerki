namespace Frimerki.Models;

public static class Constants {
    private const string ValidDomainRegexBasePattern =
        @"[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*";
    private const string ValidUsernameRegexBasePattern = "[a-zA-Z0-9._-]+";

    public const string ValidDomainRegexPattern = $"^{ValidDomainRegexBasePattern}$";
    public const string ValidDkimRegexPattern = @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$";
    public const string ValidUsernameRegexPattern = $"^{ValidUsernameRegexBasePattern}$";
    public const string ValidUserRoleRegexPattern = "^(User|DomainAdmin|HostAdmin)$";
    public const string ValidEmailRegexPattern = $"({ValidUsernameRegexBasePattern})@({ValidDomainRegexBasePattern})";
}
