namespace Frimerki.Protocols.Imap;

/// <summary>
/// IMAP connection states as defined by RFC 3501
/// </summary>
public enum ImapConnectionState {
    /// <summary>
    /// Initial connection state requiring authentication
    /// </summary>
    NotAuthenticated,

    /// <summary>
    /// User authenticated but no mailbox selected
    /// </summary>
    Authenticated,

    /// <summary>
    /// Mailbox selected for message operations
    /// </summary>
    Selected,

    /// <summary>
    /// Connection termination state
    /// </summary>
    Logout
}

/// <summary>
/// IMAP command processing results
/// </summary>
public enum ImapResponseType {
    Ok,
    No,
    Bad,
    Bye,
    Preauth
}

/// <summary>
/// IMAP response with status and message
/// </summary>
public record ImapResponse(string Tag = "",
                        ImapResponseType Type = ImapResponseType.Ok,
                            string Message = "",
                            string? ResponseCode = null) {
    public override string ToString() =>
        string.IsNullOrEmpty(ResponseCode)
            ? $"{Tag} {Type.ToString().ToUpper()} {Message}"
            : $"{Tag} {Type.ToString().ToUpper()} [{ResponseCode}] {Message}";
}

/// <summary>
/// Parsed IMAP command from client
/// </summary>
public record ImapCommand {
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string RawCommand { get; set; } = "";
}
