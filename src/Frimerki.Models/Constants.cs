namespace Frimerki.Models;

public static class Constants {
    private const string _validDomainRegex =
        @"[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*";
    private const string _validUsernameRegex = "[a-zA-Z0-9._-]+";

    public const string ValidDomainRegex = $"^{_validDomainRegex}$";
    public const string ValidDkimRegex = @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$";
    public const string ValidUsernameRegex = $"^{_validUsernameRegex}$";
    public const string ValidUserRoleRegex = "^(User|DomainAdmin|HostAdmin)$";
    public const string ValidEmailRegex = $"({_validUsernameRegex})@({_validDomainRegex})";
}
