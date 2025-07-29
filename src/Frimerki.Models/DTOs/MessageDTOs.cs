using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public class MessageRequest {
    [Required, EmailAddress]
    public string ToAddress { get; set; } = string.Empty;

    public string? CcAddress { get; set; }
    public string? BccAddress { get; set; }

    [Required, StringLength(998)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public string? BodyHtml { get; set; }
    public bool IsHtml { get; set; } = false;
    public string? InReplyTo { get; set; }
    public string? References { get; set; }
    public List<MessageAttachmentRequest>? Attachments { get; set; }
}

public class MessageAttachmentRequest {
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string ContentType { get; set; } = string.Empty;

    [Required]
    public byte[] Content { get; set; } = [];
}

public class SimpleEmailRequest {
    [Required, EmailAddress]
    public string To { get; set; } = string.Empty;

    [Required, StringLength(998)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;
}

public class MessageUpdateRequest {
    public MessageFlagsRequest? Flags { get; set; }
    public int? FolderId { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? BodyHtml { get; set; }
}

public class MessageFlagsRequest {
    public bool? Seen { get; set; }
    public bool? Answered { get; set; }
    public bool? Flagged { get; set; }
    public bool? Deleted { get; set; }
    public bool? Draft { get; set; }
    public List<string>? CustomFlags { get; set; }
}

public class MessageFilterRequest {
    public string? Q { get; set; }
    public string? Folder { get; set; }
    public int? FolderId { get; set; }
    public string? Flags { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public DateTime? Since { get; set; }
    public DateTime? Before { get; set; }
    public int? MinSize { get; set; }
    public int? MaxSize { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
    public string SortBy { get; set; } = "date";
    public string SortOrder { get; set; } = "desc";
}

public class MessageResponse {
    public int Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string? CcAddress { get; set; }
    public string? BccAddress { get; set; }
    public DateTime SentDate { get; set; }
    public DateTime ReceivedAt { get; set; }
    public int MessageSize { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? BodyHtml { get; set; }
    public string Headers { get; set; } = string.Empty;
    public MessageEnvelopeResponse Envelope { get; set; } = new();
    public MessageBodyStructureResponse BodyStructure { get; set; } = new();
    public MessageFlagsResponse Flags { get; set; } = new();
    public List<MessageAttachmentResponse> Attachments { get; set; } = new();
    public int Uid { get; set; }
    public int UidValidity { get; set; }
    public DateTime InternalDate { get; set; }
    public string? InReplyTo { get; set; }
    public string? References { get; set; }
    public string Folder { get; set; } = string.Empty;
}

public class MessageListItemResponse {
    public int Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public DateTime SentDate { get; set; }
    public MessageFlagsResponse Flags { get; set; } = new();
    public string Folder { get; set; } = string.Empty;
    public bool HasAttachments { get; set; }
    public int MessageSize { get; set; }
    public string MessageSizeFormatted { get; set; } = string.Empty;
}

public class MessageListResponse {
    public List<MessageListItemResponse> Messages { get; set; } = new();
    public MessagePaginationResponse Pagination { get; set; } = new();
    public Dictionary<string, object> AppliedFilters { get; set; } = new();
}

public class MessagePaginationResponse {
    public int Skip { get; set; }
    public int Take { get; set; }
    public int TotalCount { get; set; }
    public string? NextUrl { get; set; }
}

public class MessageEnvelopeResponse {
    public string Date { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<MessageAddressResponse> From { get; set; } = new();
    public List<MessageAddressResponse> ReplyTo { get; set; } = new();
    public List<MessageAddressResponse> To { get; set; } = new();
    public List<MessageAddressResponse> Cc { get; set; } = new();
    public List<MessageAddressResponse>? Bcc { get; set; }
    public string? InReplyTo { get; set; }
    public string MessageId { get; set; } = string.Empty;
}

public class MessageAddressResponse {
    public string? Name { get; set; }
    public string Email { get; set; } = string.Empty;
}

public class MessageBodyStructureResponse {
    public string Type { get; set; } = string.Empty;
    public string Subtype { get; set; } = string.Empty;
    public List<MessageBodyStructureResponse>? Parts { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    public string? ContentId { get; set; }
    public string? ContentDescription { get; set; }
    public string? ContentTransferEncoding { get; set; }
    public int? Size { get; set; }
}

public class MessageFlagsResponse {
    public bool Seen { get; set; }
    public bool Answered { get; set; }
    public bool Flagged { get; set; }
    public bool Deleted { get; set; }
    public bool Draft { get; set; }
    public bool Recent { get; set; }
    public List<string> CustomFlags { get; set; } = new();
}

public class MessageAttachmentResponse {
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int Size { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
