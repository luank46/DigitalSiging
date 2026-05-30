using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.Observability.HealthChecks;

/// <summary>
/// Health check that verifies Redis connectivity by sending a PING command.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer redis, ILogger<RedisHealthCheck> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var pong = await db.PingAsync();

            // Redis responds within milliseconds when healthy; a very long ping
            // indicates network or server trouble.
            if (pong.TotalMilliseconds > 5000)
            {
                _logger.LogWarning("Redis ping responded slowly: {ElapsedMs:F0}ms", pong.TotalMilliseconds);
                return HealthCheckResult.Degraded(
                    $"Redis responded slowly ({pong.TotalMilliseconds:F0}ms)");
            }

            return HealthCheckResult.Healthy(
                $"Redis reachable (ping={pong.TotalMilliseconds:F0}ms)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis is not reachable", ex);
        }
    }
}
