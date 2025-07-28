using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class Message
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string HeaderMessageId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(255)]
    public string FromAddress { get; set; } = string.Empty;
    
    [MaxLength(255)]
    public string? ToAddress { get; set; }
    
    public string? CcAddress { get; set; }
    
    public string? BccAddress { get; set; }
    
    public string? Subject { get; set; }
    
    [Required]
    public string Headers { get; set; } = string.Empty;
    
    public string? Body { get; set; }
    
    public string? BodyHtml { get; set; }
    
    public int MessageSize { get; set; }
    
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? SentDate { get; set; }
    
    public string? InReplyTo { get; set; }
    
    public string? References { get; set; }
    
    public string? BodyStructure { get; set; }
    
    public string? Envelope { get; set; }
    
    public int Uid { get; set; }
    
    public int UidValidity { get; set; } = 1;
    
    // Navigation properties
    public UidValiditySequence UidValiditySequence { get; set; } = null!;
    public ICollection<UserMessage> UserMessages { get; set; } = new List<UserMessage>();
    public ICollection<MessageFlag> MessageFlags { get; set; } = new List<MessageFlag>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
