using System.ComponentModel.DataAnnotations;

namespace MLIntegration.Models.MercadoLibre
{
    public class MLToken
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        public string AccessToken { get; set; }
        
        [Required]
        public string RefreshToken { get; set; }
        
        [Required]
        public DateTime ExpiresAt { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}