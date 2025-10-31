using System.ComponentModel.DataAnnotations;

namespace MLIntegration.Models.MercadoLibre
{
    public class MLProduct
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string ItemId { get; set; }
        
        [Required]
        public string FamilyName { get; set; }
        
        [Required]
        public string CategoryId { get; set; }
        
        [Required]
        public decimal Price { get; set; }
        
        [Required]
        public int AvailableQuantity { get; set; }
        
        [Required]
        public string Status { get; set; }
        
        public string SubStatus { get; set; }
        public DateTime? LastSync { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}