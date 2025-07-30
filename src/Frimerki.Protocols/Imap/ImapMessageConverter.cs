using System.Text.RegularExpressions;

using Frimerki.Models.Entities;
using Frimerki.Services.Message;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// Handles MIME message conversion for IMAP responses
/// </summary>
public partial class ImapMessageConverter {
    [GeneratedRegex(@"^(?:""?([^""<>]+?)""?\s*)?<([^<>]+@[^<>]+)>$|^([^<>@]+@[^<>@]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailAddressRegex();

    /// <summary>
    /// Converts a Message entity to an IMAP FETCH response
    /// </summary>
    /// <param name="message">The message to convert</param>
    /// <param name="fetchItems">The items requested in the FETCH command</param>
    /// <returns>IMAP FETCH response string</returns>
    public string ConvertToImapFetch(Message message, string[] fetchItems) {
        List<string> parts = [];

        foreach (var item in fetchItems) {
            var part = item.ToUpperInvariant() switch {
                "UID" => $"UID {message.Uid}",
                "FLAGS" => $"FLAGS ({GetImapFlags(message)})",
                "ENVELOPE" => $"ENVELOPE {GetBasicEnvelope(message)}",
                "RFC822.SIZE" => $"RFC822.SIZE {message.MessageSize}",
                "INTERNALDATE" => $"INTERNALDATE \"{message.ReceivedAt:dd-MMM-yyyy HH:mm:ss zzz}\"",
                "RFC822" => $"RFC822 {{{message.MessageSize}}}\r\n{message.Headers}\r\n\r\n{message.Body ?? ""}",
                "RFC822.HEADER" => $"RFC822.HEADER {{{message.Headers?.Length ?? 0}}}\r\n{message.Headers ?? ""}",
                "RFC822.TEXT" => $"RFC822.TEXT {{{message.Body?.Length ?? 0}}}\r\n{message.Body ?? ""}",
                "BODY" or "BODYSTRUCTURE" => $"{item} (\"text\" \"plain\" NIL NIL NIL \"7bit\" {message.Body?.Length ?? 0} NIL NIL NIL)",
                _ => null
            };

            if (part != null) {
                parts.Add(part);
            }
        }

        return string.Join(" ", parts);
    }

    private static string GetImapFlags(Message message) =>
        string.Join(" ", message.MessageFlags.Select(f => f.FlagName)
            .Where(f => MessageService.StandardFlags.ContainsKey(f))
            .ToList());

    private string GetBasicEnvelope(Message message) {
        // IMAP ENVELOPE format: (date subject from sender reply-to to cc bcc in-reply-to message-id)
        // For now, return a basic envelope structure
        var subject = ExtractField(message.Headers, "Subject");
        var from = ExtractFrom(message.Headers);

        return $"(\"{message.ReceivedAt:dd-MMM-yyyy HH:mm:ss zzz}\" " +
               $"\"{EscapeString(subject)}\" " +
               $"{from} NIL NIL NIL NIL NIL " +
               $"\"{EscapeString(message.HeaderMessageId)}\" NIL)";
    }

    private static string ExtractField(string headers, string fieldName) {
        var span = headers.AsSpan();
        string match = fieldName + ":";
        foreach (var line in span.EnumerateLines()) {
            if (line.StartsWith(match, StringComparison.OrdinalIgnoreCase)) {
                return line[match.Length..].Trim().ToString();
            }
        }
        return "";
    }

    private string ExtractFrom(string headers) {
        var fromStr = ExtractField(headers, "From");
        if (string.IsNullOrEmpty(fromStr)) {
            return "NIL";
        }

        var match = EmailAddressRegex().Match(fromStr.Trim());
        if (!match.Success) {
            return "NIL";
        }

        string name, email;

        if (match.Groups[3].Success) {
            // Simple email format: user@domain.com
            email = match.Groups[3].Value;
            name = "";
        } else {
            // Name + email format: "Name" <user@domain.com> or Name <user@domain.com>
            name = match.Groups[1].Value;
            email = match.Groups[2].Value;
        }

        if (email.Contains('@')) {
            var parts = email.Split('@', 2);
            return $"((\"{EscapeString(name)}\" NIL \"{EscapeString(parts[0])}\" \"{EscapeString(parts[1])}\"))";
        }

        return "NIL";
    }

    private string EscapeString(string input) {
        return string.IsNullOrEmpty(input) ? "" : input.Replace("\"", "\\\"");
    }
}
