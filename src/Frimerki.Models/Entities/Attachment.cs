using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public record Attachment {
    public int Id { get; init; }

    public int MessageId { get; init; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; init; } = "";

    [MaxLength(100)]
    public string ContentType { get; init; }

    public int Size { get; init; }

    [Required]
    [MaxLength(36)]
    public string FileGuid { get; init; } = "";

    [MaxLength(10)]
    public string FileExtension { get; init; }

    [MaxLength(500)]
    public string FilePath { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public Message Message { get; init; }
}
