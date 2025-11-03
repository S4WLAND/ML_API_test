// ============================================
// Servicio background: renueva tokens 15 min antes de expirar (chequea cada 10 min)
// ============================================
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MLIntegration.Helpers;

namespace MLIntegration.Services
{
    public class MLTokenRefreshService : BackgroundService
    {
        private readonly ILogger<MLTokenRefreshService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval;
        private readonly int _thresholdMinutes;

        public MLTokenRefreshService(
            ILogger<MLTokenRefreshService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            _thresholdMinutes = configuration.GetValue<int?>("MercadoLibre:RefreshThresholdMinutes") ?? 15;
            _checkInterval = TimeSpan.FromMinutes(10);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔄 Servicio de renovación automática de tokens ML iniciado");
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshExpiringTokensAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error al renovar tokens automáticamente");
                }

                _logger.LogInformation($"⏱️ Próxima verificación en {_checkInterval.TotalMinutes} min (umbral: {_thresholdMinutes} min).");
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("🛑 Servicio de renovación automática de tokens ML detenido");
        }

        private async Task RefreshExpiringTokensAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<IMLTokenService>();
            var mlHelper = scope.ServiceProvider.GetRequiredService<MercadoLibreHelper>();

            var tokensToRefresh = await tokenService.GetTokensExpiringSoonAsync(_thresholdMinutes);

            if (tokensToRefresh.Count == 0)
            {
                _logger.LogInformation("✅ No hay tokens que requieran renovación");
                return;
            }

            _logger.LogInformation($"🔑 Renovando {tokensToRefresh.Count} token(s)...");
            foreach (var token in tokensToRefresh)
            {
                try
                {
                    await mlHelper.RefreshAccessTokenAsync(token.UserId, token.RefreshToken);
                    _logger.LogInformation($"✅ Token renovado (userId: {token.UserId})");
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Error al renovar token para userId {token.UserId}");
                    if (ex.Message.Contains("invalid_grant") || ex.Message.Contains("401"))
                        _logger.LogWarning($"⚠️ Refresh token inválido (userId {token.UserId}). Reautorizar.");
                }
            }
        }
    }
}
