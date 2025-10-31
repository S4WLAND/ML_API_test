// ============================================
// Services/IMLTokenService.cs
// Interfaz del servicio de tokens
// ============================================
using System.Collections.Generic;
using System.Threading.Tasks;
using MLIntegration.Models.MercadoLibre;

namespace MLIntegration.Services
{
    public interface IMLTokenService
    {
        Task<MLToken> GetTokenAsync(int userId);
        Task SaveTokenAsync(int userId, MLAuthResponse authResponse);
        Task UpdateTokenAsync(int userId, MLAuthResponse authResponse);
        Task<bool> IsTokenExpiringSoonAsync(int userId, int minutesThreshold = 30);
        Task<List<MLToken>> GetTokensExpiringSoonAsync(int minutesThreshold = 30);
        Task DeleteTokenAsync(int userId);
    }
}