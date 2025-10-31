using Microsoft.AspNetCore.Mvc;
using MLIntegration.Helpers;
using MLIntegration.Models.MercadoLibre;
using System.Text.Json;

namespace MLIntegration.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MercadoLibreController : ControllerBase
    {
        private readonly MercadoLibreHelper _mlHelper;
        private readonly ILogger<MercadoLibreController> _logger;

        public MercadoLibreController(
            MercadoLibreHelper mlHelper,
            ILogger<MercadoLibreController> logger)
        {
            _mlHelper = mlHelper;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene la URL de autorización OAuth
        /// </summary>
        [HttpGet("auth/url")]
        public IActionResult GetAuthUrl([FromQuery] int userId)
        {
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/mercadolibre/callback";
            var authUrl = _mlHelper.GetAuthorizationUrl(redirectUri);
            
            return Ok(new { authorizationUrl = authUrl, userId });
        }

        /// <summary>
        /// Callback de autorización OAuth
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] int userId = 1)
        {
            try
            {
                var redirectUri = $"{Request.Scheme}://{Request.Host}/api/mercadolibre/callback";
                var result = await _mlHelper.ExchangeCodeForTokenAsync(code, redirectUri, userId);
                
                return Ok(new { 
                    message = "Autorización exitosa",
                    userId,
                    expiresAt = DateTime.UtcNow.AddSeconds(result.ExpiresIn)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en callback OAuth");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Crear publicación
        /// </summary>
        [HttpPost("items")]
        public async Task<IActionResult> CreateItem([FromBody] MLItemRequest request, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.CreateItemAsync(request, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear item");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Obtener item por ID
        /// </summary>
        [HttpGet("items/{itemId}")]
        public async Task<IActionResult> GetItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.GetItemAsync(itemId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Actualizar precio y cantidad
        /// </summary>
        [HttpPut("items/{itemId}/price")]
        public async Task<IActionResult> UpdatePrice(
            string itemId, 
            [FromBody] PriceUpdateRequest request,
            [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.UpdatePriceAndQuantityAsync(
                    itemId, request.Price, request.Quantity, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Pausar publicación
        /// </summary>
        [HttpPut("items/{itemId}/pause")]
        public async Task<IActionResult> PauseItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.PauseItemAsync(itemId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Eliminar publicación
        /// </summary>
        [HttpDelete("items/{itemId}")]
        public async Task<IActionResult> DeleteItem(string itemId, [FromQuery] int userId = 1)
        {
            try
            {
                var result = await _mlHelper.DeleteItemAsync(itemId, userId);
                return Ok(new { message = "Item eliminado", result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }

    public class PriceUpdateRequest
    {
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}