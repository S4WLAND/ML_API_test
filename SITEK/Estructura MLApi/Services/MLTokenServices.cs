using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YourProject.Models.MercadoLibre;
using YourProject.Data;

namespace YourProject.Services
{
    // ============================================
    // INTERFAZ
    // ============================================

    public interface IMLTokenService
    {
        Task<MLToken> GetTokenAsync(int userId);
        Task SaveTokenAsync(int userId, MLAuthResponse authResponse);
        Task UpdateTokenAsync(int userId, MLAuthResponse authResponse);
        Task<bool> IsTokenExpiringSoonAsync(int userId, int minutesThreshold = 30);
        Task<List<MLToken>> GetTokensExpiringSoonAsync(int minutesThreshold = 30);
        Task DeleteTokenAsync(int userId);
    }

    // ============================================
    // IMPLEMENTACIÓN
    // ============================================

    public class MLTokenService : IMLTokenService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MLTokenService> _logger;

        public MLTokenService(ApplicationDbContext context, ILogger<MLTokenService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene el token de un usuario
        /// </summary>
        public async Task<MLToken> GetTokenAsync(int userId)
        {
            try
            {
                return await _context.MLTokens
                    .Where(t => t.UserId == userId)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener token para usuario {userId}");
                throw;
            }
        }

        /// <summary>
        /// Guarda un nuevo token (primera vez)
        /// </summary>
        public async Task SaveTokenAsync(int userId, MLAuthResponse authResponse)
        {
            try
            {
                // Verificar si ya existe un token para este usuario
                var existingToken = await GetTokenAsync(userId);
                
                if (existingToken != null)
                {
                    // Si ya existe, actualizar
                    await UpdateTokenAsync(userId, authResponse);
                    return;
                }

                var token = new MLToken
                {
                    UserId = userId,
                    AccessToken = authResponse.AccessToken,
                    RefreshToken = authResponse.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.MLTokens.Add(token);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Token guardado para usuario {userId}, expira en {authResponse.ExpiresIn} segundos");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al guardar token para usuario {userId}");
                throw;
            }
        }

        /// <summary>
        /// Actualiza el token existente
        /// </summary>
        public async Task UpdateTokenAsync(int userId, MLAuthResponse authResponse)
        {
            try
            {
                var token = await GetTokenAsync(userId);

                if (token == null)
                {
                    await SaveTokenAsync(userId, authResponse);
                    return;
                }

                token.AccessToken = authResponse.AccessToken;
                token.RefreshToken = authResponse.RefreshToken;
                token.ExpiresAt = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn);
                token.UpdatedAt = DateTime.UtcNow;

                _context.MLTokens.Update(token);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Token actualizado para usuario {userId}, nueva expiración: {token.ExpiresAt}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar token para usuario {userId}");
                throw;
            }
        }

        /// <summary>
        /// Verifica si el token de un usuario está por expirar
        /// </summary>
        public async Task<bool> IsTokenExpiringSoonAsync(int userId, int minutesThreshold = 30)
        {
            try
            {
                var token = await GetTokenAsync(userId);
                
                if (token == null)
                    return false;

                return token.ExpiresAt <= DateTime.UtcNow.AddMinutes(minutesThreshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar expiración de token para usuario {userId}");
                throw;
            }
        }

        /// <summary>
        /// Obtiene todos los tokens que están por expirar (para renovación automática)
        /// </summary>
        public async Task<List<MLToken>> GetTokensExpiringSoonAsync(int minutesThreshold = 30)
        {
            try
            {
                var expirationDate = DateTime.UtcNow.AddMinutes(minutesThreshold);
                
                return await _context.MLTokens
                    .Where(t => t.ExpiresAt <= expirationDate)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener tokens que expiran pronto");
                throw;
            }
        }

        /// <summary>
        /// Elimina el token de un usuario (por ejemplo, al desconectar cuenta)
        /// </summary>
        public async Task DeleteTokenAsync(int userId)
        {
            try
            {
                var token = await GetTokenAsync(userId);
                
                if (token != null)
                {
                    _context.MLTokens.Remove(token);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Token eliminado para usuario {userId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al eliminar token para usuario {userId}");
                throw;
            }
        }
    }
}