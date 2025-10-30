using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace YourProject.Models.MercadoLibre
{
    // ============================================
    // ENTIDAD DE BASE DE DATOS
    // ============================================

    /// <summary>
    /// Entidad para almacenar tokens OAuth de Mercado Libre
    /// </summary>
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

    // ============================================
    // DTOs DE RESPUESTA DE LA API
    // ============================================

    /// <summary>
    /// Respuesta de autenticación OAuth
    /// </summary>
    public class MLAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; }

        [JsonPropertyName("user_id")]
        public long UserId { get; set; }
    }

    // ============================================
    // DTOs DE REQUEST
    // ============================================

    /// <summary>
    /// Request para crear/actualizar un ítem
    /// </summary>
    public class MLItemRequest
    {
        [JsonPropertyName("family_name")]
        public string FamilyName { get; set; }

        [JsonPropertyName("category_id")]
        public string CategoryId { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("currency_id")]
        public string CurrencyId { get; set; } = "ARS";

        [JsonPropertyName("available_quantity")]
        public int AvailableQuantity { get; set; }

        [JsonPropertyName("buying_mode")]
        public string BuyingMode { get; set; } = "buy_it_now";

        [JsonPropertyName("listing_type_id")]
        public string ListingTypeId { get; set; } = "gold_special";

        [JsonPropertyName("condition")]
        public string Condition { get; set; } = "used";

        [JsonPropertyName("channels")]
        public List<string> Channels { get; set; } = new List<string> { "marketplace" };

        [JsonPropertyName("pictures")]
        public List<MLPicture> Pictures { get; set; }

        [JsonPropertyName("attributes")]
        public List<MLAttribute> Attributes { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("warranty")]
        public string Warranty { get; set; }

        [JsonPropertyName("video_id")]
        public string VideoId { get; set; }

        [JsonPropertyName("sale_terms")]
        public List<MLSaleTerm> SaleTerms { get; set; }
    }

    /// <summary>
    /// Imagen del producto
    /// </summary>
    public class MLPicture
    {
        [JsonPropertyName("source")]
        public string Source { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    /// <summary>
    /// Atributo del producto (ej: marca, modelo)
    /// </summary>
    public class MLAttribute
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("value_id")]
        public string ValueId { get; set; }

        [JsonPropertyName("value_name")]
        public string ValueName { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Término de venta (ej: garantía, política de devoluciones)
    /// </summary>
    public class MLSaleTerm
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("value_name")]
        public string ValueName { get; set; }

        [JsonPropertyName("value_struct")]
        public object ValueStruct { get; set; }
    }

    // ============================================
    // ENTIDAD DE PRODUCTO SINCRONIZADO
    // ============================================

    /// <summary>
    /// Producto sincronizado con Mercado Libre
    /// </summary>
    public class MLProduct
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ItemId { get; set; } // MLA1234567890

        [Required]
        public string FamilyName { get; set; }

        [Required]
        [MaxLength(20)]
        public string CategoryId { get; set; }

        [Required]
        public decimal Price { get; set; }

        [Required]
        public int AvailableQuantity { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } // active, paused, closed

        public string SubStatus { get; set; } // deleted, expired, etc.

        public DateTime? LastSync { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ============================================
    // ENUMS Y CONSTANTES
    // ============================================

    /// <summary>
    /// Estados posibles de una publicación
    /// </summary>
    public static class MLItemStatus
    {
        public const string Active = "active";
        public const string Paused = "paused";
        public const string Closed = "closed";
    }

    /// <summary>
    /// Tipos de listado
    /// </summary>
    public static class MLListingType
    {
        public const string GoldSpecial = "gold_special";
        public const string GoldPro = "gold_pro";
        public const string Gold = "gold";
        public const string Silver = "silver";
        public const string Bronze = "bronze";
        public const string Free = "free";
    }

    /// <summary>
    /// Condiciones del producto
    /// </summary>
    public static class MLCondition
    {
        public const string New = "new";
        public const string Used = "used";
        public const string Refurbished = "refurbished";
        public const string NotSpecified = "not_specified";
    }

    /// <summary>
    /// Sitios de Mercado Libre
    /// </summary>
    public static class MLSite
    {
        public const string Argentina = "MLA";
        public const string Brasil = "MLB";
        public const string Chile = "MLC";
        public const string Colombia = "MCO";
        public const string CostaRica = "MCR";
        public const string Ecuador = "MEC";
        public const string Mexico = "MLM";
        public const string Panama = "MPA";
        public const string Peru = "MPE";
        public const string Uruguay = "MLU";
        public const string Venezuela = "MLV";
    }
}