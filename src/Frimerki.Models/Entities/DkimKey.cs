using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class DkimKey
{
    public int Id { get; set; }
    
    public int DomainId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Selector { get; set; } = string.Empty;
    
    [Required]
    public string PrivateKey { get; set; } = string.Empty;
    
    [Required]
    public string PublicKey { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public Domain Domain { get; set; } = null!;
}
