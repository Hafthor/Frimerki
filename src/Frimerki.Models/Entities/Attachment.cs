using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class Attachment {
    public int Id { get; set; }

    public int MessageId { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; }

    public int Size { get; set; }

    [Required]
    [MaxLength(36)]
    public string FileGuid { get; set; } = "";

    [MaxLength(10)]
    public string FileExtension { get; set; }

    [MaxLength(500)]
    public string FilePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Message Message { get; set; }
}
