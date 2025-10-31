// ============================================
// Services/MLTokenRefreshService.cs
// Servicio de background para renovación automática
// ============================================
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MLIntegration.Helpers;

namespace MLIntegration.Services
{
    /// <summary>
    /// Servicio de background que renueva automáticamente los tokens de Mercado Libre
    /// antes de que expiren
    /// </summary>
    public class MLTokenRefreshService : BackgroundService
    {
        private readonly ILogger<MLTokenRefreshService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(5); // Verificar cada 5 horas

        public MLTokenRefreshService(
            ILogger<MLTokenRefreshService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Método principal que se ejecuta en background
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔄 Servicio de renovación automática de tokens ML iniciado");

            // Esperar 1 minuto antes de la primera ejecución (dar tiempo a que la app inicie)
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

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

                // Esperar hasta la próxima verificación
                _logger.LogInformation($"⏱️  Próxima verificación en {_checkInterval.TotalHours} horas");
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("🛑 Servicio de renovación automática de tokens ML detenido");
        }

        /// <summary>
        /// Busca y renueva todos los tokens que están por expirar
        /// </summary>
        private async Task RefreshExpiringTokensAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<IMLTokenService>();
            var mlHelper = scope.ServiceProvider.GetRequiredService<MercadoLibreHelper>();

            try
            {
                // Obtener tokens que expiran en menos de 30 minutos
                var tokensToRefresh = await tokenService.GetTokensExpiringSoonAsync(minutesThreshold: 30);

                if (tokensToRefresh.Count == 0)
                {
                    _logger.LogInformation("✅ No hay tokens que requieran renovación");
                    return;
                }

                _logger.LogInformation($"🔄 Renovando {tokensToRefresh.Count} token(s)...");

                foreach (var token in tokensToRefresh)
                {
                    try
                    {
                        _logger.LogInformation($"🔑 Renovando token para usuario {token.UserId}, expira en: {token.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
                        
                        await mlHelper.RefreshAccessTokenAsync(token.UserId, token.RefreshToken);
                        
                        _logger.LogInformation($"✅ Token renovado exitosamente para usuario {token.UserId}");
                        
                        // Pequeña pausa entre renovaciones para no saturar la API
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Error al renovar token para usuario {token.UserId}");
                        
                        // Si el refresh token es inválido, se debe notificar al usuario
                        // para que vuelva a autorizar la aplicación
                        if (ex.Message.Contains("invalid_grant") || ex.Message.Contains("401"))
                        {
                            _logger.LogWarning($"⚠️  El refresh token del usuario {token.UserId} es inválido. Se requiere reautorización.");
                            // Aquí podrías enviar una notificación al usuario
                            // await _notificationService.NotifyReauthorizationNeededAsync(token.UserId);
                        }
                    }
                }

                _logger.LogInformation("✅ Proceso de renovación de tokens completado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error general en el proceso de renovación de tokens");
                throw;
            }
        }

        /// <summary>
        /// Se ejecuta al detener el servicio
        /// </summary>
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🛑 Deteniendo servicio de renovación de tokens...");
            await base.StopAsync(stoppingToken);
        }
    }
}