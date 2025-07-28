namespace Frimerki.Models.Entities;

public class UserMessage
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int MessageId { get; set; }
    
    public int FolderId { get; set; }
    
    public int Uid { get; set; }
    
    public int? SequenceNumber { get; set; }
    
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public Message Message { get; set; } = null!;
    public Folder Folder { get; set; } = null!;
}
