using System.Text;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// Parses IMAP commands according to RFC 3501 syntax
/// </summary>
public class ImapCommandParser {
    public ImapCommand? ParseCommand(string commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return null;
        }

        var trimmed = commandLine.Trim();
        List<string> parts = [];
        StringBuilder currentPart = new();
        var inQuotes = false;
        var inLiteral = false;
        var literalLength = 0;

        for (int i = 0; i < trimmed.Length; i++) {
            var ch = trimmed[i];

            if (inLiteral) {
                currentPart.Append(ch);
                literalLength--;
                if (literalLength == 0) {
                    inLiteral = false;
                }
                continue;
            }

            switch (ch) {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                    if (currentPart.Length > 0) {
                        parts.Add(currentPart.ToString());
                        currentPart.Clear();
                    }
                    break;
                case '{' when !inQuotes:
                    // Handle literal syntax {length}
                    var closeBrace = trimmed.IndexOf('}', i);
                    if (closeBrace > i && int.TryParse(trimmed[(i + 1)..closeBrace], out literalLength)) {
                        inLiteral = true;
                        i = closeBrace;
                    } else {
                        currentPart.Append(ch);
                    }
                    break;
                default:
                    currentPart.Append(ch);
                    break;
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
    public string UnquoteString(string quotedString) {
        if (string.IsNullOrEmpty(quotedString)) {
            return quotedString;
        }

        if (quotedString.Length < 2 || !quotedString.StartsWith('"') || !quotedString.EndsWith('"')) {
            return quotedString;
        }

        return quotedString[1..^1];
    }

    /// <summary>
    /// Parses a sequence set (e.g., "1,3:5,7:*")
    /// </summary>
    public List<uint> ParseSequenceSet(string sequenceSet, uint maxSequence) {
        List<uint> result = [];
        var parts = sequenceSet.Split(',');

        foreach (var part in parts) {
            if (part.Contains(':')) {
                var range = part.Split(':');
                if (range.Length == 2) {
                    var start = range[0] == "*" ? maxSequence : uint.Parse(range[0]);
                    var end = range[1] == "*" ? maxSequence : uint.Parse(range[1]);

                    for (uint i = Math.Min(start, end); i <= Math.Max(start, end) && i <= maxSequence; i++) {
                        result.Add(i);
                    }
                }
            } else {
                var num = part == "*" ? maxSequence : uint.Parse(part);
                if (num <= maxSequence) {
                    result.Add(num);
                }
            }
        }

        return result.Distinct().OrderBy(x => x).ToList();
    }
}
