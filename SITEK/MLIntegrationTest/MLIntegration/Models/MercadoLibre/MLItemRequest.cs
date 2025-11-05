using System.Text.Json.Serialization;

namespace MLIntegration.Models.MercadoLibre
{
    public class MLItemRequest
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

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
        public List<string> Channels { get; set; } = new() { "marketplace" };

        [JsonPropertyName("pictures")]
        public List<MLPicture> Pictures { get; set; }

        [JsonPropertyName("attributes")]
        public List<MLAttribute> Attributes { get; set; }
    }

    public class MLPicture
    {
        [JsonPropertyName("source")]
        public string Source { get; set; }
    }

    public class MLAttribute
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("value_id")]
        public string? ValueId { get; set; }

        [JsonPropertyName("value_name")]
        public string ValueName { get; set; }
    }
}