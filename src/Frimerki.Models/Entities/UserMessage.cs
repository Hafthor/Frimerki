namespace Frimerki.Models.Entities;

public record UserMessage {
    public int Id { get; init; }

    public int UserId { get; init; }

    public int MessageId { get; init; }

    public int FolderId { get; set; }

    public int Uid { get; set; }

    public int SequenceNumber { get; init; }

    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; init; }
    public Message Message { get; init; }
    public Folder Folder { get; init; }
}
