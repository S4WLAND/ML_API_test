using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MLIntegration.Models.MercadoLibre;
using MLIntegration.Services;
using MLIntegration.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace MLIntegration.Helpers
{
    public class MercadoLibreHelper
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MercadoLibreHelper> _logger;
        private readonly IMLTokenService _tokenService;
        private readonly ApplicationDbContext _db;

        private const string API_BASE_URL = "https://api.mercadolibre.com";
        private const int TOKEN_RENEW_THRESHOLD_MIN = 15;

        public MercadoLibreHelper(
            HttpClient httpClient,
            ILogger<MercadoLibreHelper> logger,
            IMLTokenService tokenService,
            ApplicationDbContext db)
        {
            _httpClient = httpClient;
            _logger = logger;
            _tokenService = tokenService;
            _db = db;
        }

        #region OAuth - Renovación centralizada

        /// <summary>
        /// MÉTODO CENTRAL de renovación de tokens
        /// Llamado por:
        /// 1. Background Service (proactivo - cada 10 min)
        /// 2. GetValidAccessToken (reactivo - si el background falló)
        /// </summary>
        public async Task<MLAuthResponse> RefreshAccessTokenAsync(int userId, string refreshToken)
        {
            // Leer de variables de entorno
            var clientId = Environment.GetEnvironmentVariable("APP_ID");
            var clientSecret = Environment.GetEnvironmentVariable("SECRET_KEY");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new InvalidOperationException("❌ APP_ID o SECRET_KEY no configurados en .env");
            }

            var parameters = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "refresh_token", refreshToken }
            };

            _logger.LogInformation($"🔄 Renovando token para userId: {userId}");

            var response = await _httpClient.PostAsync($"{API_BASE_URL}/oauth/token", new FormUrlEncodedContent(parameters));
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Error renovando token para userId {userId}: {response.StatusCode}");
                _logger.LogError($"Response: {content}");
                
                // Si es invalid_grant, significa que el refresh_token expiró o es inválido
                if (content.Contains("invalid_grant"))
                {
                    throw new UnauthorizedAccessException(
                        $"⚠️ Refresh token inválido o expirado para userId {userId}. " +
                        "Debe obtener un nuevo refresh_token manualmente y ejecutar /tokens/seed"
                    );
                }

                throw new HttpRequestException($"Error al renovar token: {response.StatusCode} - {content}");
            }

            var authResponse = JsonSerializer.Deserialize<MLAuthResponse>(
                content, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            // Actualizar en BD
            await _tokenService.UpdateTokenAsync(userId, authResponse);
            _logger.LogInformation($"✅ Token renovado exitosamente (userId: {userId}, expires_in: {authResponse.ExpiresIn}s)");

            return authResponse;
        }

        /// <summary>
        /// Obtiene un access token válido para hacer requests
        /// - Si está válido (>15 min): lo devuelve
        /// - Si está por expirar (≤15 min): lo renueva inline
        /// 
        /// Este es el ÚNICO método que debe llamarse antes de cada request a ML
        /// </summary>
        private async Task<string> GetValidAccessTokenAsync(int userId)
        {
            var tokenData = await _tokenService.GetTokenAsync(userId);
            
            if (tokenData == null)
            {
                throw new UnauthorizedAccessException(
                    $"❌ No hay tokens disponibles para userId {userId}. " +
                    "Ejecutar POST /tokens/seed primero con un refresh_token válido."
                );
            }

            var now = DateTime.UtcNow;
            var timeUntilExpiry = tokenData.ExpiresAt - now;

            // ✅ Token válido: usarlo directamente
            if (timeUntilExpiry.TotalMinutes > TOKEN_RENEW_THRESHOLD_MIN)
            {
                _logger.LogDebug($"✅ Token válido para userId {userId} (expira en {timeUntilExpiry.TotalMinutes:F1} min)");
                return tokenData.AccessToken;
            }

            // ⚠️ Token por expirar o expirado: renovar inline (fallback si el background falló)
            _logger.LogWarning(
                $"⚠️ Token por expirar para userId {userId} (expira en {timeUntilExpiry.TotalMinutes:F1} min). " +
                "Renovando inline como fallback..."
            );

            try
            {
                var newAuth = await RefreshAccessTokenAsync(userId, tokenData.RefreshToken);
                return newAuth.AccessToken;
            }
            catch (UnauthorizedAccessException)
            {
                // Re-lanzar para que el controller maneje el error
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error fatal renovando token inline para userId {userId}");
                throw new InvalidOperationException(
                    $"No se pudo renovar el token para userId {userId}. " +
                    "Verifique conectividad a MercadoLibre API o ejecute /tokens/seed con un nuevo refresh_token.",
                    ex
                );
            }
        }

        #endregion

        #region GET

        public async Task<JsonDocument> GetItemAsync(string itemId, int userId)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);
            var result = await ExecuteGetRequestAsync($"/items/{itemId}", accessToken);
            await UpsertProductFromRemote(userId, result);
            return result;
        }

        public async Task<JsonDocument> GetCategoriesAsync(string siteId = "MLA")
            => await ExecuteGetRequestAsync($"/sites/{siteId}/categories");

        public async Task<JsonDocument> PredictCategoryAsync(string title, string siteId = "MLA")
        {
            var encodedTitle = Uri.EscapeDataString(title);
            return await ExecuteGetRequestAsync($"/sites/{siteId}/domain_discovery/search?q={encodedTitle}");
        }

        public async Task<JsonDocument> GetCategoryAttributesAsync(string categoryId)
            => await ExecuteGetRequestAsync($"/categories/{categoryId}/attributes");

        /// <summary>
        /// Obtiene los IDs de todos los productos activos del usuario desde MercadoLibre
        /// </summary>
        public async Task<List<string>> GetUserItemsAsync(int userId)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            // Obtener ML User ID
            var userResponse = await _httpClient.GetAsync($"{API_BASE_URL}/users/me");
            if (!userResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Error obteniendo usuario: {userResponse.StatusCode}");
            }

            var userContent = await userResponse.Content.ReadAsStringAsync();
            var userDoc = JsonDocument.Parse(userContent);
            var mlUserId = userDoc.RootElement.GetProperty("id").GetInt64();

            _logger.LogInformation($"🔍 Buscando productos para ML User ID: {mlUserId}");

            // Buscar productos activos
            var searchResponse = await _httpClient.GetAsync(
                $"{API_BASE_URL}/users/{mlUserId}/items/search?status=active&limit=50"
            );
            
            if (!searchResponse.IsSuccessStatusCode)
            {
                var errorContent = await searchResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Error al buscar productos: {searchResponse.StatusCode} - {errorContent}");
            }

            var searchContent = await searchResponse.Content.ReadAsStringAsync();
            var searchDoc = JsonDocument.Parse(searchContent);
            
            var itemIds = new List<string>();
            if (searchDoc.RootElement.TryGetProperty("results", out var results))
            {
                itemIds = results.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }

            _logger.LogInformation($"📦 Encontrados {itemIds.Count} productos activos");
            return itemIds;
        }

        #endregion

        #region POST

        public async Task<JsonDocument> CreateItemAsync(MLItemRequest itemRequest, int userId)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);
            var json = JsonSerializer.Serialize(itemRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.PostAsync($"{API_BASE_URL}/items", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Error creando item: {response.StatusCode} - {responseContent}");
                throw new HttpRequestException($"Error {response.StatusCode}: {responseContent}");
            }

            var result = JsonDocument.Parse(responseContent);
            await UpsertProductFromRemote(userId, result);
            
            _logger.LogInformation($"✅ Item creado exitosamente: {result.RootElement.GetProperty("id").GetString()}");
            return result;
        }

        #endregion

        #region PUT

        public async Task<JsonDocument> UpdateItemAsync(string itemId, object updates, int userId)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);

            var json = JsonSerializer.Serialize(updates, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.PutAsync($"{API_BASE_URL}/items/{itemId}", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Error actualizando item {itemId}: {response.StatusCode} - {responseContent}");
                throw new HttpRequestException($"Error {response.StatusCode}: {responseContent}");
            }

            var result = JsonDocument.Parse(responseContent);
            await UpsertProductFromRemote(userId, result);
            return result;
        }

        public async Task<JsonDocument> PauseItemAsync(string itemId, int userId)
            => await UpdateItemAsync(itemId, new { status = "paused" }, userId);

        public async Task<JsonDocument> ActivateItemAsync(string itemId, int userId)
            => await UpdateItemAsync(itemId, new { status = "active" }, userId);

        public async Task<JsonDocument> UpdatePriceAndQuantityAsync(string itemId, decimal price, int quantity, int userId)
            => await UpdateItemAsync(itemId, new { price, available_quantity = quantity }, userId);

        #endregion

        #region DELETE (soft delete)

        public async Task<JsonDocument> DeleteItemAsync(string itemId, int userId)
        {
            _logger.LogInformation($"🗑️ Eliminando item {itemId} para userId {userId}");

            // 1️⃣ Cerrar publicación en MercadoLibre
            await UpdateItemAsync(itemId, new { status = "closed" }, userId);
            _logger.LogInformation($"⏳ Esperando propagación del estado closed...");
            await Task.Delay(2500);

            // 2️⃣ Marcar como eliminada remotamente
            var result = await UpdateItemAsync(itemId, new { deleted = "true" }, userId);

            // 3️⃣ Actualizar BD local
            var local = await _db.MLProducts.FirstOrDefaultAsync(p => p.UserId == userId && p.ItemId == itemId);
            if (local != null)
            {
                local.IsDeleted = true;
                local.DeletedAt = DateTime.UtcNow;
                local.Status = "closed";
                local.UpdatedAt = DateTime.UtcNow;
                _db.MLProducts.Update(local);
                await _db.SaveChangesAsync();
                _logger.LogInformation($"✅ Item {itemId} marcado como eliminado en BD local");
            }

            return result;
        }

        #endregion

        #region Sync

        /// <summary>
        /// Sincroniza todos los productos del usuario desde MercadoLibre
        /// </summary>
        public async Task<SyncResult> SyncAllUserProductsAsync(int userId)
        {
            var result = new SyncResult();
            
            try
            {
                _logger.LogInformation($"🔄 Iniciando sincronización masiva para userId: {userId}");
                
                var itemIds = await GetUserItemsAsync(userId);
                result.TotalProducts = itemIds.Count;

                if (itemIds.Count == 0)
                {
                    _logger.LogWarning($"⚠️ No se encontraron productos activos para userId {userId}");
                    return result;
                }

                _logger.LogInformation($"📦 Sincronizando {itemIds.Count} productos...");

                foreach (var itemId in itemIds)
                {
                    try
                    {
                        _logger.LogDebug($"⏳ Procesando {itemId}...");
                        
                        // GetItemAsync ya guarda/actualiza en BD automáticamente
                        await GetItemAsync(itemId, userId);
                        
                        var existingProduct = await _db.MLProducts
                            .FirstOrDefaultAsync(p => p.UserId == userId && p.ItemId == itemId);

                        if (existingProduct != null && existingProduct.CreatedAt == existingProduct.UpdatedAt)
                        {
                            result.Created++;
                        }
                        else
                        {
                            result.Updated++;
                        }

                        // Rate limiting: 200ms entre requests (máx 5 req/s)
                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Error procesando producto {itemId}");
                        result.Errors.Add($"{itemId}: {ex.Message}");
                        result.Skipped++;
                    }
                }

                _logger.LogInformation(
                    $"✅ Sincronización completada: " +
                    $"{result.Created} creados, {result.Updated} actualizados, {result.Skipped} omitidos"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error fatal durante sincronización para userId {userId}");
                throw;
            }

            return result;
        }

        #endregion

        #region Aux

        private async Task UpsertProductFromRemote(int userId, JsonDocument remote)
        {
            var root = remote.RootElement;
            var itemId = root.GetProperty("id").GetString();
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : "active";
            var subStatus = root.TryGetProperty("sub_status", out var sub) ? sub.ToString() : "[]";
            var price = root.TryGetProperty("price", out var p) ? p.GetDecimal() : 0m;
            var qty = root.TryGetProperty("available_quantity", out var q) ? q.GetInt32() : 0;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var categoryId = root.TryGetProperty("category_id", out var c) ? c.GetString() : null;

            var product = await _db.MLProducts.FirstOrDefaultAsync(x => x.UserId == userId && x.ItemId == itemId);

            if (product == null)
            {
                product = new MLProduct
                {
                    UserId = userId,
                    ItemId = itemId,
                    FamilyName = title ?? "N/A",
                    CategoryId = categoryId ?? "N/A",
                    Price = price,
                    AvailableQuantity = qty,
                    Status = status,
                    SubStatus = subStatus,
                    IsDeleted = subStatus.Contains("deleted", StringComparison.OrdinalIgnoreCase),
                    DeletedAt = subStatus.Contains("deleted", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastSync = DateTime.UtcNow
                };
                _db.MLProducts.Add(product);
            }
            else
            {
                product.Price = price;
                product.AvailableQuantity = qty;
                product.Status = status;
                product.SubStatus = subStatus;
                product.UpdatedAt = DateTime.UtcNow;
                product.LastSync = DateTime.UtcNow;

                var nowDeleted = subStatus.Contains("deleted", StringComparison.OrdinalIgnoreCase);
                if (nowDeleted && !product.IsDeleted)
                {
                    product.IsDeleted = true;
                    product.DeletedAt = DateTime.UtcNow;
                }

                _db.MLProducts.Update(product);
            }

            await _db.SaveChangesAsync();
        }

        private async Task<JsonDocument> ExecuteGetRequestAsync(string endpoint, string accessToken = null)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(accessToken))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.GetAsync($"{API_BASE_URL}{endpoint}");
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Error {response.StatusCode}: {content}");

            return JsonDocument.Parse(content);
        }

        #endregion
    }

    public class SyncResult
    {
        public int TotalProducts { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}