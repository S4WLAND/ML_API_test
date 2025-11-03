using System.ComponentModel.DataAnnotations;

namespace MLIntegration.Models.MercadoLibre
{
    public class MLProduct
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required, MaxLength(50)]
        public string ItemId { get; set; }

        [Required]
        public string FamilyName { get; set; }

        [Required]
        public string CategoryId { get; set; }

        [Required]
        public decimal Price { get; set; }

        [Required]
        public int AvailableQuantity { get; set; }

        /// Estado remoto (active / paused / closed)
        [Required]
        public string Status { get; set; }

        /// Subestado remoto (ej: ["deleted"])
        public string SubStatus { get; set; }

        /// Soft delete local (reflejo de sub_status deleted)
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }

        public DateTime? LastSync { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
