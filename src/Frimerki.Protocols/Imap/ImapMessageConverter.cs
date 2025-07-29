using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Frimerki.Models.Entities;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// Handles MIME message conversion for IMAP responses
/// </summary>
public class ImapMessageConverter {
    private static readonly HashSet<string> StandardImapFlags = new() {
        "\\Seen", "\\Answered", "\\Flagged", "\\Deleted", "\\Draft"
    };
    /// <summary>
    /// Converts a Message entity to an IMAP FETCH response
    /// </summary>
    /// <param name="message">The message to convert</param>
    /// <param name="fetchItems">The items requested in the FETCH command</param>
    /// <returns>IMAP FETCH response string</returns>
    public string ConvertToImapFetch(Message message, string[] fetchItems) {
        List<string> parts = [];

        foreach (var item in fetchItems) {
            switch (item.ToUpperInvariant()) {
                case "UID":
                    parts.Add($"UID {message.Uid}");
                    break;
                case "FLAGS":
                    parts.Add($"FLAGS ({GetImapFlags(message)})");
                    break;
                case "ENVELOPE":
                    parts.Add($"ENVELOPE {GetBasicEnvelope(message)}");
                    break;
                case "RFC822.SIZE":
                    parts.Add($"RFC822.SIZE {message.MessageSize}");
                    break;
                case "INTERNALDATE":
                    parts.Add($"INTERNALDATE \"{message.ReceivedAt:dd-MMM-yyyy HH:mm:ss zzz}\"");
                    break;
                case "RFC822":
                    parts.Add($"RFC822 {{{message.MessageSize}}}\r\n{message.Headers}\r\n\r\n{message.Body ?? ""}");
                    break;
                case "RFC822.HEADER":
                    parts.Add($"RFC822.HEADER {{{message.Headers?.Length ?? 0}}}\r\n{message.Headers ?? ""}");
                    break;
                case "RFC822.TEXT":
                    parts.Add($"RFC822.TEXT {{{message.Body?.Length ?? 0}}}\r\n{message.Body ?? ""}");
                    break;
                case "BODY":
                case "BODYSTRUCTURE":
                    parts.Add($"{item} (\"text\" \"plain\" NIL NIL NIL \"7bit\" {message.Body?.Length ?? 0} NIL NIL NIL)");
                    break;
            }
        }

        return string.Join(" ", parts);
    }

    private string GetImapFlags(Message message) =>
        string.Join(" ", message.MessageFlags?
            .Select(f => f.FlagName)
            .Where(f => StandardImapFlags.Contains(f))
            .ToList() ?? []);

    private string GetBasicEnvelope(Message message) {
        // IMAP ENVELOPE format: (date subject from sender reply-to to cc bcc in-reply-to message-id)
        // For now, return a basic envelope structure
        var subject = ExtractSubject(message.Headers ?? "");
        var from = ExtractFrom(message.Headers ?? "");

        return $"(\"{message.ReceivedAt:dd-MMM-yyyy HH:mm:ss zzz}\" " +
               $"\"{EscapeString(subject)}\" " +
               $"{from} NIL NIL NIL NIL NIL " +
               $"\"{EscapeString(message.HeaderMessageId)}\" NIL)";
    }

    private string ExtractSubject(string headers) {
        var span = headers.AsSpan();
        foreach (var line in span.EnumerateLines()) {
            if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)) {
                return line[8..].Trim().ToString();
            }
        }
        return "";
    }

    private string ExtractFrom(string headers) {
        var span = headers.AsSpan();
        foreach (var line in span.EnumerateLines()) {
            if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)) {
                var from = line[5..].Trim();
                // Basic email parsing - could be enhanced
                var fromStr = from.ToString(); // Convert to string for Contains/IndexOf operations
                if (fromStr.Contains("<") && fromStr.Contains(">")) {
                    var emailStart = fromStr.IndexOf('<') + 1;
                    var emailEnd = fromStr.IndexOf('>');
                    var email = fromStr[emailStart..emailEnd];
                    var name = fromStr[..fromStr.IndexOf('<')].Trim().Trim('"');
                    if (email.Contains("@")) {
                        var parts = email.Split('@');
                        return $"((\"{EscapeString(name)}\" NIL \"{EscapeString(parts[0])}\" \"{EscapeString(parts[1])}\"))";
                    }
                }
            }
        }
        return "NIL";
    }

    private string EscapeString(string input) {
        if (string.IsNullOrEmpty(input)) {
            return "";
        }
        return input.Replace("\"", "\\\"");
    }
}
