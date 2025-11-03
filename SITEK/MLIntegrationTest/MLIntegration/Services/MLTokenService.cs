// ============================================
// Services/MLTokenService.cs
// Implementación del servicio de tokens (ciclo de vida)
// ============================================
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MLIntegration.Models.MercadoLibre;
using MLIntegration.Data;

namespace MLIntegration.Services
{
    public class MLTokenService : IMLTokenService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MLTokenService> _logger;

        public MLTokenService(ApplicationDbContext context, ILogger<MLTokenService> logger)
        {
            _context = context;
            _logger = logger;
        }

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

        public async Task SaveTokenAsync(int userId, MLAuthResponse authResponse)
        {
            try
            {
                var existing = await GetTokenAsync(userId);
                var now = DateTime.UtcNow;

                if (existing != null)
                {
                    await UpdateTokenAsync(userId, authResponse);
                    return;
                }

                var token = new MLToken
                {
                    UserId = userId,
                    AccessToken = authResponse.AccessToken,
                    RefreshToken = authResponse.RefreshToken,
                    IssuedAt = now,
                    RefreshTokenIssuedAt = now,
                    LastRefreshedAt = null,
                    RefreshCount = 0,
                    ExpiresAt = now.AddSeconds(authResponse.ExpiresIn),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.MLTokens.Add(token);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Token guardado para usuario {userId}, expira en {authResponse.ExpiresIn} s");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al guardar token para usuario {userId}");
                throw;
            }
        }

        public async Task UpdateTokenAsync(int userId, MLAuthResponse authResponse)
        {
            try
            {
                var token = await GetTokenAsync(userId);
                var now = DateTime.UtcNow;

                if (token == null)
                {
                    await SaveTokenAsync(userId, authResponse);
                    return;
                }

                var isRefreshRotation = !string.IsNullOrEmpty(authResponse.RefreshToken)
                                        && authResponse.RefreshToken != token.RefreshToken;

                token.AccessToken = authResponse.AccessToken;
                token.RefreshToken = authResponse.RefreshToken ?? token.RefreshToken;
                token.IssuedAt = now;
                token.ExpiresAt = now.AddSeconds(authResponse.ExpiresIn);
                token.UpdatedAt = now;

                if (isRefreshRotation)
                {
                    token.RefreshTokenIssuedAt = now;
                    token.LastRefreshedAt = now;
                    token.RefreshCount += 1;
                }

                _context.MLTokens.Update(token);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Token actualizado para user {userId}. Expira: {token.ExpiresAt:O}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar token para usuario {userId}");
                throw;
            }
        }

        public async Task<bool> IsTokenExpiringSoonAsync(int userId, int minutesThreshold = 30)
        {
            try
            {
                var token = await GetTokenAsync(userId);
                if (token == null) return false;
                return token.ExpiresAt <= DateTime.UtcNow.AddMinutes(minutesThreshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar expiración de token para usuario {userId}");
                throw;
            }
        }

        public async Task<List<MLToken>> GetTokensExpiringSoonAsync(int minutesThreshold = 30)
        {
            try
            {
                var limit = DateTime.UtcNow.AddMinutes(minutesThreshold);
                return await _context.MLTokens.Where(t => t.ExpiresAt <= limit).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener tokens que expiran pronto");
                throw;
            }
        }

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
