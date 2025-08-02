using System.Text;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// Parses IMAP commands according to RFC 3501 syntax
/// </summary>
public static class ImapCommandParser {
    public static ImapCommand? ParseCommand(string commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return null;
        }

        var trimmed = commandLine.Trim();
        List<string> parts = [];
        StringBuilder currentPart = new();
        var inQuotes = false;
        var literalLength = 0;

        for (int i = 0; i < trimmed.Length; i++) {
            var ch = trimmed[i];

            if (literalLength > 0) {
                currentPart.Append(ch);
                literalLength--;
            } else if (ch == '"') {
                inQuotes = !inQuotes;
            } else if (inQuotes) {
                currentPart.Append(ch);
            } else if (ch == ' ') {
                if (currentPart.Length > 0) {
                    parts.Add(currentPart.ToString());
                    currentPart.Clear();
                }
            } else if (ch == '{') {
                // Handle literal syntax {length}
                var closeBrace = trimmed.IndexOf('}', i);
                if (closeBrace > i && int.TryParse(trimmed[(i + 1)..closeBrace], out literalLength)) {
                    i = closeBrace;
                } else {
                    currentPart.Append(ch);
                }
            } else {
                currentPart.Append(ch);
            }
        }

        if (currentPart.Length > 0) {
            parts.Add(currentPart.ToString());
        }

        if (parts.Count < 2) {
            return null;
        }

        return new ImapCommand {
            Tag = parts[0],
            Name = parts[1].ToUpper(),
            Arguments = parts.Skip(2).ToList(),
            RawCommand = commandLine
        };
    }

    /// <summary>
    /// Unquotes a quoted string argument
    /// </summary>
    public static string UnquoteString(string quotedString) {
        if (string.IsNullOrEmpty(quotedString) ||
            quotedString.Length < 2 || !quotedString.StartsWith('"') || !quotedString.EndsWith('"')) {
            return quotedString;
        }

        return quotedString[1..^1];
    }
}
