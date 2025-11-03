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

        /// Fecha/hora (UTC) en que se emitió el access_token actual.
        [Required]
        public DateTime IssuedAt { get; set; }

        /// Fecha/hora (UTC) en que se emitió el refresh_token actual.
        public DateTime? RefreshTokenIssuedAt { get; set; }

        /// Fecha/hora (UTC) del último refresh exitoso.
        public DateTime? LastRefreshedAt { get; set; }

        /// Cantidad de rotaciones realizadas (refresh flow exitoso).
        [Required]
        public int RefreshCount { get; set; } = 0;

        /// Fecha/hora (UTC) en que expira el access_token actual.
        [Required]
        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
