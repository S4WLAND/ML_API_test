using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MLIntegration.Data;
using MLIntegration.Helpers;
using MLIntegration.Models.MercadoLibre;

namespace MLIntegration.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MercadoLibreController : ControllerBase
    {
        private readonly MercadoLibreHelper _mlHelper;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MercadoLibreController> _logger;

        public MercadoLibreController(
            MercadoLibreHelper mlHelper,
            ApplicationDbContext context,
            ILogger<MercadoLibreController> logger)
        {
            _mlHelper = mlHelper;
            _context = context;
            _logger = logger;
        }

        // ======================
        // 🧰 ADMIN - Token Seed
        // ======================

        /// <summary>
        /// Inserta o actualiza el refresh_token inicial en la BD
        /// Este es el ÚNICO punto de entrada para configurar tokens
        /// Proceso manual previo:
        /// 1. Obtener refresh_token desde MercadoLibre API manualmente
        /// 2. Llamar a este endpoint con ese refresh_token
        /// 3. El sistema renovará el access_token automáticamente
        /// </summary>
        [HttpPost("tokens/seed")]
        public async Task<IActionResult> SeedInitialToken([FromBody] SeedTokenRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken))
                {
                    return BadRequest(new { error = "refresh_token es requerido" });
                }

                var token = await _context.MLTokens.FirstOrDefaultAsync(t => t.UserId == request.UserId);

                if (token == null)
                {
                    token = new MLToken
                    {
                        UserId = request.UserId,
                        AccessToken = string.Empty, // Se llenará en primera renovación
                        RefreshToken = request.RefreshToken,
                        IssuedAt = DateTime.UtcNow,
                        RefreshTokenIssuedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow, // Expirado para forzar renovación inmediata
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        RefreshCount = 0
                    };
                    _context.MLTokens.Add(token);
                    _logger.LogInformation($"🆕 Nuevo token creado para userId: {request.UserId}");
                }
                else
                {
                    token.RefreshToken = request.RefreshToken;
                    token.RefreshTokenIssuedAt = DateTime.UtcNow;
                    token.ExpiresAt = DateTime.UtcNow; // Expirado para forzar renovación inmediata
                    token.UpdatedAt = DateTime.UtcNow;
                    _context.MLTokens.Update(token);
                    _logger.LogInformation($"🔄 Token actualizado para userId: {request.UserId}");
                }

                await _context.SaveChangesAsync();
                
                return Ok(new 
                { 
                    message = "✅ Token inicial registrado correctamente",
                    userId = request.UserId,
                    note = "El access_token se renovará automáticamente en la primera petición o por el servicio de background"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error al registrar token inicial");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Obtiene el estado actual de los tokens
        /// </summary>
        [HttpGet("tokens/status")]
        public async Task<IActionResult> GetTokenStatus([FromQuery] int userId = 1)
        {
            try
            {
                var token = await _context.MLTokens.FirstOrDefaultAsync(t => t.UserId == userId);
                
                if (token == null)
                {
                    return NotFound(new 
                    { 
                        error = "No se encontró token para este usuario",
                        userId,
                        action = "Ejecutar POST /tokens/seed con un refresh_token válido"
                    });
                }

                var now = DateTime.UtcNow;
                var accessTokenExpiry = (token.ExpiresAt - now).TotalHours;
                var refreshTokenAge = token.RefreshTokenIssuedAt.HasValue 
                    ? (now - token.RefreshTokenIssuedAt.Value).TotalDays 
                    : 0;

                // Refresh token expira a los 180 días (6 meses)
                var refreshTokenDaysLeft = 180 - refreshTokenAge;

                return Ok(new
                {
                    userId = token.UserId,
                    accessToken = new
                    {
                        exists = !string.IsNullOrEmpty(token.AccessToken),
                        expiresAt = token.ExpiresAt,
                        hoursUntilExpiry = accessTokenExpiry,
                        isExpired = accessTokenExpiry <= 0,
                        willRenewSoon = accessTokenExpiry <= 0.25 // 15 minutos
                    },
                    refreshToken = new
                    {
                        exists = !string.IsNullOrEmpty(token.RefreshToken),
                        issuedAt = token.RefreshTokenIssuedAt,
                        ageInDays = refreshTokenAge,
                        estimatedDaysLeft = refreshTokenDaysLeft,
                        needsReauthorization = refreshTokenDaysLeft < 30
                    },
                    statistics = new
                    {
                        refreshCount = token.RefreshCount,
                        lastRefreshed = token.LastRefreshedAt,
                        createdAt = token.CreatedAt,
                        updatedAt = token.UpdatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estado de tokens");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ======================
        // 🔄 SINCRONIZACIÓN
        // ======================

        /// <summary>
        /// Sincroniza todos los productos activos del usuario desde MercadoLibre
        /// </summary>
        [HttpPost("sync/products")]
        public async Task<IActionResult> SyncUserProducts([FromQuery] int userId = 1)
        {
            try
            {
                _logger.LogInformation($"🔄 Iniciando sincronización para userId: {userId}");
                var syncResult = await _mlHelper.SyncAllUserProductsAsync(userId);

                return Ok(new
                {
                    message = "✅ Sincronización completada",
                    productsFound = syncResult.TotalProducts,
                    productsCreated = syncResult.Created,
                    productsUpdated = syncResult.Updated,
                    productsSkipped = syncResult.Skipped,
                    errors = syncResult.Errors.Count > 0 ? syncResult.Errors : null
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Error de autorización al sincronizar");
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al sincronizar productos");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Obtiene el estado de sincronización de los productos locales
        /// </summary>
        [HttpGet("sync/status")]
        public async Task<IActionResult> GetSyncStatus([FromQuery] int userId = 1)
        {
            try
            {
                var totalLocal = await _context.MLProducts.CountAsync(p => p.UserId == userId);
                var activeLocal = await _context.MLProducts.CountAsync(p => p.UserId == userId && p.Status == "active");
                var pausedLocal = await _context.MLProducts.CountAsync(p => p.UserId == userId && p.Status == "paused");
                var deletedLocal = await _context.MLProducts.CountAsync(p => p.UserId == userId && p.IsDeleted);
                var lastSync = await _context.MLProducts
                    .Where(p => p.UserId == userId && p.LastSync.HasValue)
                    .OrderByDescending(p => p.LastSync)
                    .Select(p => p.LastSync)
                    .FirstOrDefaultAsync();

                // ✅ CAMBIAR ESTA LÍNEA
                double? hoursSinceLastSync = lastSync.HasValue 
                    ? (DateTime.UtcNow - lastSync.Value).TotalHours 
                    : null;

                return Ok(new
                {
                    totalProducts = totalLocal,
                    breakdown = new
                    {
                        active = activeLocal,
                        paused = pausedLocal,
                        deleted = deletedLocal
                    },
                    lastSyncDate = lastSync,
                    hoursSinceLastSync = hoursSinceLastSync, // Ahora puede ser null
                    needsSync = lastSync == null || (hoursSinceLastSync.HasValue && hoursSinceLastSync.Value > 24),
                    recommendation = lastSync == null 
                        ? "Ejecutar primera sincronización: POST /sync/products" 
                        : (hoursSinceLastSync.HasValue && hoursSinceLastSync.Value > 24)
                            ? "Recomendado sincronizar (última sync hace más de 24h)" 
                            : "Sincronización reciente"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estado de sincronización");
                return BadRequest(new { error = ex.Message });
            }
        }

        // ======================
        // 🧩 CRUD PUBLICACIONES
        // ======================

        [HttpPost("items")]
        public async Task<IActionResult> CreateItem([FromBody] MLItemRequest request, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.CreateItemAsync(request, userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear item");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("items/{itemId}")]
        public async Task<IActionResult> GetItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.GetItemAsync(itemId, userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPut("items/{itemId}/price")]
        public async Task<IActionResult> UpdatePrice(
            string itemId,
            [FromBody] PriceUpdateRequest request,
            [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.UpdatePriceAndQuantityAsync(
                    itemId,
                    request.Price,
                    request.Quantity,
                    userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("items/{itemId}/pause")]
        public async Task<IActionResult> PauseItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.PauseItemAsync(itemId, userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("items/{itemId}/activate")]
        public async Task<IActionResult> ActivateItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.ActivateItemAsync(itemId, userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("items/{itemId}")]
        public async Task<IActionResult> DeleteItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.DeleteItemAsync(itemId, userId);
                return Ok(new { message = "✅ Item eliminado (soft delete)", result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar item");
                return BadRequest(new { error = ex.Message });
            }
        }
    }

    // ======================
    // 📸 CLASES AUXILIARES
    // ======================

    public class PriceUpdateRequest
    {
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    public class SeedTokenRequest
    {
        public int UserId { get; set; }
        public string RefreshToken { get; set; }
    }
}