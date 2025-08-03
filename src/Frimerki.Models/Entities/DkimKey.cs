using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class DkimKey {
    public int Id { get; set; }

    public int DomainId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Selector { get; set; } = "";

    [Required]
    public string PrivateKey { get; set; } = "";

    [Required]
    public string PublicKey { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public DomainSettings Domain { get; set; }
}
