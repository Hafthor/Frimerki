using System.ComponentModel.DataAnnotations;
using MimeKit;

namespace Frimerki.Models.DTOs;

public record MessageRequest {
    [Required, EmailAddress]
    public string ToAddress { get; init; } = "";

    public string CcAddress { get; init; }
    public string BccAddress { get; set; }

    [Required, StringLength(998)]
    public string Subject { get; init; } = "";

    [Required]
    public string Body { get; init; } = "";

    public string BodyHtml { get; set; }
    public bool IsHtml { get; init; }
    public string InReplyTo { get; init; }
    public string References { get; init; }
    public List<MessageAttachmentRequest> Attachments { get; init; }
}

public record MessageAttachmentRequest {
    [Required]
    public string Name { get; init; } = "";

    [Required]
    public string ContentType { get; init; } = "";

    [Required]
    public byte[] Content { get; init; } = [];
}

public record SimpleEmailRequest {
    [Required, EmailAddress]
    public string To { get; init; } = "";

    [Required, StringLength(998)]
    public string Subject { get; init; } = "";

    [Required]
    public string Body { get; init; } = "";
}

public record MessageUpdateRequest {
    public MessageFlagsRequest Flags { get; init; }
    public int? FolderId { get; init; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public string BodyHtml { get; set; }
}

public record MessageFlagsRequest {
    public bool? Seen { get; set; }
    public bool? Answered { get; set; }
    public bool? Flagged { get; set; }
    public bool? Deleted { get; set; }
    public bool? Draft { get; set; }
    public List<string> CustomFlags { get; set; }
}

public record MessageFilterRequest {
    public string Q { get; init; }
    public string Folder { get; init; }
    public int? FolderId { get; set; }
    public string Flags { get; init; }
    public string From { get; set; }
    public string To { get; set; }
    public DateTime? Since { get; set; }
    public DateTime? Before { get; set; }
    public int? MinSize { get; set; }
    public int? MaxSize { get; set; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
    public string SortBy { get; set; } = "date";
    public string SortOrder { get; set; } = "desc";
}

public record MessageResponse {
    public int Id { get; init; }
    public string Subject { get; init; } = "";
    public string FromAddress { get; init; } = "";
    public string ToAddress { get; init; } = "";
    public string CcAddress { get; init; }
    public string BccAddress { get; init; }
    public DateTime SentDate { get; init; }
    public DateTime ReceivedAt { get; init; }
    public int MessageSize { get; init; }
    public string Body { get; init; } = "";
    public string BodyHtml { get; init; }
    public string Headers { get; init; } = "";
    public MessageEnvelopeResponse Envelope { get; init; } = new();
    public MessageBodyStructureResponse BodyStructure { get; init; } = new();
    public MessageFlagsResponse Flags { get; init; } = new();
    public List<MessageAttachmentResponse> Attachments { get; init; } = [];
    public int Uid { get; init; }
    public int UidValidity { get; init; }
    public DateTime InternalDate { get; init; }
    public string InReplyTo { get; init; }
    public string References { get; init; }
    public string Folder { get; init; } = "";
}

public record MessageListItemResponse {
    public int Id { get; init; }
    public string Subject { get; init; } = "";
    public string FromAddress { get; init; } = "";
    public string ToAddress { get; set; } = "";
    public DateTime SentDate { get; set; }
    public MessageFlagsResponse Flags { get; set; } = new();
    public string Folder { get; init; } = "";
    public bool HasAttachments { get; set; }
    public int MessageSize { get; init; }
    public string MessageSizeFormatted { get; set; } = "";
}

// MessageListResponse is now replaced by PaginatedInfo<MessageListItemResponse>
// MessagePaginationResponse is now replaced by PaginatedInfo<T>

public record MessageEnvelopeResponse {
    public string Date { get; init; } = "";
    public string Subject { get; init; } = "";
    public List<MessageAddressResponse> From { get; init; } = [];
    public List<MessageAddressResponse> ReplyTo { get; init; } = [];
    public List<MessageAddressResponse> To { get; init; } = [];
    public List<MessageAddressResponse> Cc { get; init; } = [];
    public List<MessageAddressResponse> Bcc { get; init; }
    public string InReplyTo { get; init; }
    public string MessageId { get; init; } = "";
}

public record MessageAddressResponse {
    public string Name { get; init; }
    public string Email { get; init; } = "";

    public MessageAddressResponse() { }

    public MessageAddressResponse(InternetAddress address) {
        if (address is MailboxAddress mailbox) {
            Name = mailbox.Name;
            Email = mailbox.Address;
        } else {
            Email = address.ToString();
        }
    }
}

public record MessageBodyStructureResponse {
    public string Type { get; init; } = "";
    public string Subtype { get; init; } = "";
    public List<MessageBodyStructureResponse> Parts { get; init; }
    public Dictionary<string, string> Parameters { get; init; }
    public string ContentId { get; init; }
    public string ContentDescription { get; init; }
    public string ContentTransferEncoding { get; init; }
    public int Size { get; init; }
}

public record MessageFlagsResponse {
    public bool Seen { get; init; }
    public bool Answered { get; init; }
    public bool Flagged { get; init; }
    public bool Deleted { get; init; }
    public bool Draft { get; init; }
    public bool Recent { get; init; }
    public List<string> CustomFlags { get; init; } = [];
}

public record MessageAttachmentResponse {
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public int Size { get; init; }
    public string SizeFormatted { get; init; } = "";
    public string Path { get; init; } = "";
}
