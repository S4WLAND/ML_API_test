using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
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
            // ✅ Leer de variables de entorno
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
        /// Obtiene TODOS los IDs de productos del usuario (maneja paginación automáticamente)
        /// </summary>
        public async Task<List<string>> GetUserItemsAsync(int userId)
        {
            var allIds = new List<string>();
            var accessToken = await GetValidAccessTokenAsync(userId);
            var mlUserId = Environment.GetEnvironmentVariable("ML_USER_ID");
            
            if (string.IsNullOrEmpty(mlUserId))
            {
                throw new InvalidOperationException("❌ ML_USER_ID no configurado en .env");
            }

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            _logger.LogInformation($"🔍 Obteniendo productos para ML User ID: {mlUserId}");

            // Primera request para saber el total
            var firstResponse = await _httpClient.GetAsync(
                $"{API_BASE_URL}/users/{mlUserId}/items/search?limit=50&status=active");
            
            if (!firstResponse.IsSuccessStatusCode)
            {
                var errorContent = await firstResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Error al buscar productos: {firstResponse.StatusCode} - {errorContent}");
            }

            var firstContent = await firstResponse.Content.ReadAsStringAsync();
            var firstDoc = JsonDocument.Parse(firstContent);
            
            var total = firstDoc.RootElement.GetProperty("paging").GetProperty("total").GetInt32();
            _logger.LogInformation($"📊 Total de productos encontrados: {total}");

            // Agregar resultados de la primera página
            if (firstDoc.RootElement.TryGetProperty("results", out var firstResults))
            {
                var firstIds = firstResults.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(id => !string.IsNullOrEmpty(id));
                allIds.AddRange(firstIds);
            }

            // Si hay más de 50 productos, paginar
            if (total > 50)
            {
                // Si total <= 1000, usar offset
                if (total <= 1000)
                {
                    _logger.LogInformation($"📄 Usando paginación con offset (total: {total})");
                    
                    for (int offset = 50; offset < total; offset += 50)
                    {
                        _logger.LogDebug($"📄 Obteniendo página: offset={offset}");
                        
                        var response = await _httpClient.GetAsync(
                            $"{API_BASE_URL}/users/{mlUserId}/items/search?limit=50&offset={offset}&status=active");
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogWarning($"⚠️ Error en página offset={offset}: {response.StatusCode}");
                            continue;
                        }
                        
                        var content = await response.Content.ReadAsStringAsync();
                        var doc = JsonDocument.Parse(content);
                        
                        if (doc.RootElement.TryGetProperty("results", out var results))
                        {
                            var ids = results.EnumerateArray()
                                .Select(item => item.GetString())
                                .Where(id => !string.IsNullOrEmpty(id));
                            allIds.AddRange(ids);
                        }
                        
                        await Task.Delay(200); // Rate limiting
                    }
                }
                else
                {
                    // Si total > 1000, usar scroll API
                    _logger.LogInformation($"🔄 Usando scroll API (total: {total})");
                    
                    string scrollId = null;
                    bool hasMore = true;
                    int pageCount = 1;
                    
                    // Primera request con search_type=scan (ya tenemos los primeros 50, empezar desde página 2)
                    var scanUrl = $"{API_BASE_URL}/users/{mlUserId}/items/search?search_type=scan&limit=100&status=active";
                    var response = await _httpClient.GetAsync(scanUrl);
                    var content = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(content);
                    
                    while (hasMore && pageCount < 100) // Límite de seguridad
                    {
                        pageCount++;
                        
                        if (doc.RootElement.TryGetProperty("results", out var results))
                        {
                            var ids = results.EnumerateArray()
                                .Select(item => item.GetString())
                                .Where(id => !string.IsNullOrEmpty(id))
                                .ToList();
                            
                            if (ids.Count == 0)
                            {
                                _logger.LogInformation($"✅ No hay más resultados en página {pageCount}");
                                hasMore = false;
                                break;
                            }
                            
                            allIds.AddRange(ids);
                            _logger.LogDebug($"📄 Scroll página {pageCount}: {ids.Count} items");
                        }
                        
                        // Obtener scroll_id para siguiente página
                        if (doc.RootElement.TryGetProperty("scroll_id", out var scrollIdProp))
                        {
                            scrollId = scrollIdProp.GetString();
                            
                            if (string.IsNullOrEmpty(scrollId))
                            {
                                _logger.LogInformation($"✅ scroll_id vacío, fin de resultados");
                                hasMore = false;
                                break;
                            }
                            
                            await Task.Delay(200); // Rate limiting
                            
                            // Siguiente página
                            response = await _httpClient.GetAsync(
                                $"{API_BASE_URL}/users/{mlUserId}/items/search?scroll_id={scrollId}&limit=100");
                            
                            if (!response.IsSuccessStatusCode)
                            {
                                _logger.LogWarning($"⚠️ Error en scroll página {pageCount}: {response.StatusCode}");
                                hasMore = false;
                                break;
                            }
                            
                            content = await response.Content.ReadAsStringAsync();
                            doc = JsonDocument.Parse(content);
                        }
                        else
                        {
                            _logger.LogInformation($"✅ No hay scroll_id, fin de resultados");
                            hasMore = false;
                        }
                    }
                    
                    if (pageCount >= 100)
                    {
                        _logger.LogWarning($"⚠️ Alcanzado límite de seguridad de 100 páginas");
                    }
                }
            }

            _logger.LogInformation($"✅ Total de IDs obtenidos: {allIds.Count}");
            return allIds;
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

                // 1. Obtener todos los IDs
                var itemIds = await GetUserItemsAsync(userId);
                result.TotalProducts = itemIds.Count;

                if (itemIds.Count == 0)
                {
                    _logger.LogWarning($"⚠️ No se encontraron productos activos para userId {userId}");
                    return result;
                }

                _logger.LogInformation($"📦 Sincronizando {itemIds.Count} productos en lotes de 20...");

                // 2. Procesar en lotes de 20 con Multiget
                var batches = itemIds.Chunk(20).ToList();
                int batchNumber = 0;

                foreach (var batch in batches)
                {
                    batchNumber++;
                    _logger.LogDebug($"📦 Procesando lote {batchNumber}/{batches.Count} ({batch.Count()} items)");

                    try
                    {
                        var ids = string.Join(",", batch);
                        var accessToken = await GetValidAccessTokenAsync(userId);

                        _httpClient.DefaultRequestHeaders.Clear();
                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                        // Multiget: hasta 20 items por request
                        var response = await _httpClient.GetAsync($"{API_BASE_URL}/items?ids={ids}");
                        var content = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogError($"❌ Error en lote {batchNumber}: {response.StatusCode} - {content}");
                            result.Skipped += batch.Count();
                            result.Errors.Add($"Lote {batchNumber}: HTTP {response.StatusCode}");
                            continue;
                        }

                        // Parse respuesta multiget
                        var multigetResponse = JsonSerializer.Deserialize<List<JsonElement>>(content);

                        foreach (var item in multigetResponse)
                        {
                            try
                            {
                                var code = item.GetProperty("code").GetInt32();

                                if (code == 200)
                                {
                                    var body = item.GetProperty("body");
                                    var itemId = body.GetProperty("id").GetString();

                                    // Convertir a JsonDocument para reutilizar UpsertProductFromRemote
                                    var itemDoc = JsonDocument.Parse(body.GetRawText());
                                    await UpsertProductFromRemote(userId, itemDoc);

                                    // Verificar si fue creado o actualizado
                                    var existingProduct = await _db.MLProducts
                                        .FirstOrDefaultAsync(p => p.UserId == userId && p.ItemId == itemId);

                                    if (existingProduct != null)
                                    {
                                        var timeDiff = (existingProduct.UpdatedAt - existingProduct.CreatedAt).TotalSeconds;
                                        if (timeDiff < 1)
                                        {
                                            result.Created++;
                                            _logger.LogDebug($"✅ Creado: {itemId}");
                                        }
                                        else
                                        {
                                            result.Updated++;
                                            _logger.LogDebug($"🔄 Actualizado: {itemId}");
                                        }
                                    }
                                }
                                else
                                {
                                    var itemId = item.GetProperty("body").TryGetProperty("id", out var id)
                                        ? id.GetString()
                                        : "unknown";
                                    result.Skipped++;
                                    result.Errors.Add($"{itemId}: HTTP {code}");
                                    _logger.LogWarning($"⚠️ Item {itemId} retornó código {code}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "❌ Error procesando item individual en lote");
                                result.Skipped++;
                                result.Errors.Add($"Item en lote {batchNumber}: {ex.Message}");
                            }
                        }

                        // Rate limiting: 200ms entre lotes
                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Error procesando lote {batchNumber}");
                        result.Skipped += batch.Count();
                        result.Errors.Add($"Lote {batchNumber}: {ex.Message}");
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