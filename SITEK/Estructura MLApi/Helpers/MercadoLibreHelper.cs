using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace YourProject.Helpers
{
    /// <summary>
    /// Helper para interactuar con la API de Mercado Libre
    /// Maneja autenticación OAuth 2.0 y operaciones CRUD
    /// </summary>
    public class MercadoLibreHelper
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MercadoLibreHelper> _logger;
        private readonly IMLTokenService _tokenService;

        // Constantes de la API
        private const string API_BASE_URL = "https://api.mercadolibre.com";
        private const string AUTH_URL = "https://auth.mercadolibre.com.ar";

        public MercadoLibreHelper(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<MercadoLibreHelper> logger,
            IMLTokenService tokenService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _tokenService = tokenService;
        }

        #region OAuth 2.0 - Autenticación

        /// <summary>
        /// Genera la URL de autorización para que el usuario autorice la aplicación
        /// </summary>
        public string GetAuthorizationUrl(string redirectUri)
        {
            var clientId = _configuration["MercadoLibre:ClientId"];
            return $"{AUTH_URL}/authorization?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        /// <summary>
        /// Intercambia el código de autorización por tokens de acceso
        /// </summary>
        public async Task<MLAuthResponse> ExchangeCodeForTokenAsync(string code, string redirectUri, int userId)
        {
            try
            {
                var clientId = _configuration["MercadoLibre:ClientId"];
                var clientSecret = _configuration["MercadoLibre:ClientSecret"];

                var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "code", code },
                    { "redirect_uri", redirectUri }
                };

                var response = await _httpClient.PostAsync(
                    $"{API_BASE_URL}/oauth/token",
                    new FormUrlEncodedContent(parameters)
                );

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<MLAuthResponse>(content, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                // Guardar tokens en BD
                await _tokenService.SaveTokenAsync(userId, authResponse);

                _logger.LogInformation($"Tokens obtenidos exitosamente para usuario {userId}");
                return authResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al intercambiar código por tokens");
                throw;
            }
        }

        /// <summary>
        /// Renueva el access token usando el refresh token
        /// </summary>
        public async Task<MLAuthResponse> RefreshAccessTokenAsync(int userId, string refreshToken)
        {
            try
            {
                var clientId = _configuration["MercadoLibre:ClientId"];
                var clientSecret = _configuration["MercadoLibre:ClientSecret"];

                var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "refresh_token", refreshToken }
                };

                var response = await _httpClient.PostAsync(
                    $"{API_BASE_URL}/oauth/token",
                    new FormUrlEncodedContent(parameters)
                );

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<MLAuthResponse>(content, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                // Actualizar tokens en BD
                await _tokenService.UpdateTokenAsync(userId, authResponse);

                _logger.LogInformation($"Token renovado exitosamente para usuario {userId}");
                return authResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al renovar token para usuario {userId}");
                throw;
            }
        }

        /// <summary>
        /// Obtiene un access token válido (renueva automáticamente si está por expirar)
        /// </summary>
        private async Task<string> GetValidAccessTokenAsync(int userId)
        {
            var tokenData = await _tokenService.GetTokenAsync(userId);

            if (tokenData == null)
            {
                throw new UnauthorizedAccessException("No hay tokens disponibles para este usuario");
            }

            // Si expira en menos de 5 minutos, renovar
            if (tokenData.ExpiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation($"Token por expirar, renovando para usuario {userId}");
                var newAuth = await RefreshAccessTokenAsync(userId, tokenData.RefreshToken);
                return newAuth.AccessToken;
            }

            return tokenData.AccessToken;
        }

        #endregion

        #region GET - Consultas

        /// <summary>
        /// Obtiene información de un ítem por su ID
        /// </summary>
        public async Task<JsonDocument> GetItemAsync(string itemId, int userId)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);
            return await ExecuteGetRequestAsync($"/items/{itemId}", accessToken);
        }

        /// <summary>
        /// Obtiene todas las categorías de un sitio
        /// </summary>
        public async Task<JsonDocument> GetCategoriesAsync(string siteId = "MLA")
        {
            return await ExecuteGetRequestAsync($"/sites/{siteId}/categories");
        }

        /// <summary>
        /// Predice la categoría basándose en el título del producto
        /// </summary>
        public async Task<JsonDocument> PredictCategoryAsync(string title, string siteId = "MLA")
        {
            var encodedTitle = Uri.EscapeDataString(title);
            return await ExecuteGetRequestAsync($"/sites/{siteId}/domain_discovery/search?q={encodedTitle}");
        }

        /// <summary>
        /// Obtiene los atributos requeridos para una categoría
        /// </summary>
        public async Task<JsonDocument> GetCategoryAttributesAsync(string categoryId)
        {
            return await ExecuteGetRequestAsync($"/categories/{categoryId}/attributes");
        }

        /// <summary>
        /// Lista los ítems de un usuario con filtros opcionales
        /// </summary>
        public async Task<JsonDocument> GetUserItemsAsync(int userId, string status = "active", int offset = 0, int limit = 50)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);
            var mlUserId = await GetMercadoLibreUserIdAsync(userId);
            return await ExecuteGetRequestAsync(
                $"/users/{mlUserId}/items/search?status={status}&offset={offset}&limit={limit}",
                accessToken
            );
        }

        /// <summary>
        /// Obtiene el user_id de Mercado Libre desde el access token
        /// </summary>
        private async Task<long> GetMercadoLibreUserIdAsync(int userId)
        {
            var accessToken = await GetValidAccessTokenAsync(userId);
            var response = await ExecuteGetRequestAsync("/users/me", accessToken);
            return response.RootElement.GetProperty("id").GetInt64();
        }

        #endregion

        #region POST - Crear publicación

        /// <summary>
        /// Crea una nueva publicación en Mercado Libre
        /// </summary>
        public async Task<JsonDocument> CreateItemAsync(MLItemRequest itemRequest, int userId)
        {
            try
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
                    _logger.LogError($"Error al crear ítem: {response.StatusCode} - {responseContent}");
                    throw new HttpRequestException($"Error {response.StatusCode}: {responseContent}");
                }

                _logger.LogInformation($"Ítem creado exitosamente para usuario {userId}");
                return JsonDocument.Parse(responseContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear publicación");
                throw;
            }
        }

        #endregion

        #region PUT - Actualizar publicación

        /// <summary>
        /// Actualiza un ítem existente
        /// </summary>
        public async Task<JsonDocument> UpdateItemAsync(string itemId, object updates, int userId)
        {
            try
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
                    _logger.LogError($"Error al actualizar ítem {itemId}: {response.StatusCode} - {responseContent}");
                    throw new HttpRequestException($"Error {response.StatusCode}: {responseContent}");
                }

                _logger.LogInformation($"Ítem {itemId} actualizado exitosamente");
                return JsonDocument.Parse(responseContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar ítem {itemId}");
                throw;
            }
        }

        /// <summary>
        /// Pausa una publicación
        /// </summary>
        public async Task<JsonDocument> PauseItemAsync(string itemId, int userId)
        {
            return await UpdateItemAsync(itemId, new { status = "paused" }, userId);
        }

        /// <summary>
        /// Activa una publicación pausada
        /// </summary>
        public async Task<JsonDocument> ActivateItemAsync(string itemId, int userId)
        {
            return await UpdateItemAsync(itemId, new { status = "active" }, userId);
        }

        /// <summary>
        /// Actualiza precio y cantidad
        /// </summary>
        public async Task<JsonDocument> UpdatePriceAndQuantityAsync(string itemId, decimal price, int quantity, int userId)
        {
            return await UpdateItemAsync(itemId, new { price, available_quantity = quantity }, userId);
        }

        #endregion

        #region DELETE - Eliminar publicación

        /// <summary>
        /// Elimina una publicación (proceso de 2 pasos)
        /// </summary>
        public async Task<JsonDocument> DeleteItemAsync(string itemId, int userId)
        {
            try
            {
                // Paso 1: Cerrar la publicación
                _logger.LogInformation($"Cerrando publicación {itemId}...");
                await UpdateItemAsync(itemId, new { status = "closed" }, userId);

                // Esperar propagación (ML procesa de forma asíncrona)
                await Task.Delay(2500);

                // Paso 2: Marcar como eliminada
                _logger.LogInformation($"Eliminando publicación {itemId}...");
                var result = await UpdateItemAsync(itemId, new { deleted = "true" }, userId);

                _logger.LogInformation($"Publicación {itemId} eliminada exitosamente");
                return result;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("409"))
            {
                _logger.LogWarning($"Conflicto al eliminar {itemId}, reintentando en 3 segundos...");
                await Task.Delay(3000);
                return await UpdateItemAsync(itemId, new { deleted = "true" }, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al eliminar publicación {itemId}");
                throw;
            }
        }

        #endregion

        #region Métodos auxiliares

        /// <summary>
        /// Ejecuta una petición GET genérica
        /// </summary>
        private async Task<JsonDocument> ExecuteGetRequestAsync(string endpoint, string accessToken = null)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                
                if (!string.IsNullOrEmpty(accessToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }

                var response = await _httpClient.GetAsync($"{API_BASE_URL}{endpoint}");
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Error en GET {endpoint}: {response.StatusCode} - {content}");
                    throw new HttpRequestException($"Error {response.StatusCode}: {content}");
                }

                return JsonDocument.Parse(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en petición GET a {endpoint}");
                throw;
            }
        }

        #endregion
    }
}