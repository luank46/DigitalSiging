using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using DigitalSigning.Core.Settings;
using System.Threading.Tasks;

namespace DigitalSigning.Core.HealthChecks;

/// <summary>
/// Health check that verifies RabbitMQ connectivity.
/// </summary>
public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqHealthCheck> _logger;

    public RabbitMqHealthCheck(RabbitMqSettings settings, ILogger<RabbitMqHealthCheck> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.Host,
                Port = _settings.Port,
                UserName = _settings.UserName,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            return HealthCheckResult.Healthy("RabbitMQ is reachable");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ health check failed");
            return HealthCheckResult.Unhealthy("RabbitMQ is not reachable", ex);
        }
    }
}
