using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public record Message {
    public int Id { get; init; }

    [Required]
    [MaxLength(255)]
    public string HeaderMessageId { get; init; } = "";

    [Required]
    [MaxLength(255)]
    public string FromAddress { get; init; } = "";

    [MaxLength(255)]
    public string ToAddress { get; init; }

    public string CcAddress { get; init; }

    public string BccAddress { get; init; }

    public string Subject { get; set; }

    [Required]
    public string Headers { get; init; } = "";

    public string Body { get; set; }

    public string BodyHtml { get; set; }

    public int MessageSize { get; set; }

    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;

    public DateTime? SentDate { get; init; }

    public string InReplyTo { get; init; }

    public string References { get; init; }

    public string BodyStructure { get; init; }

    public string Envelope { get; init; }

    public int Uid { get; init; }

    public int UidValidity { get; init; } = 1;

    // Navigation properties
    public UidValiditySequence UidValiditySequence { get; init; }
    public ICollection<UserMessage> UserMessages { get; init; } = [];
    public ICollection<MessageFlag> MessageFlags { get; init; } = [];
    public ICollection<Attachment> Attachments { get; init; } = [];
}
