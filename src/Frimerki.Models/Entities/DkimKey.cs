using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public record DkimKey {
    public int Id { get; init; }

    public int DomainId { get; init; }

    [Required]
    [MaxLength(50)]
    public string Selector { get; init; } = "";

    [Required]
    public string PrivateKey { get; init; } = "";

    [Required]
    public string PublicKey { get; init; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public DomainSettings Domain { get; init; }
}
