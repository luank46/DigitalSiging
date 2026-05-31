using global::MongoDB.Bson;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.HealthChecks;

/// <summary>
/// Health check that verifies MongoDB connectivity by running a ping command
/// and monitoring connection pool usage.
///
/// Healthy: ping OK + pool usage < 80%
/// Degraded: ping OK + pool usage >= 80%
/// Unhealthy: ping failed
/// </summary>
public class MongoDbHealthCheck : IHealthCheck
{
    private readonly MongoDbSettings _settings;
    private readonly ILogger<MongoDbHealthCheck> _logger;

    public MongoDbHealthCheck(MongoDbSettings settings, ILogger<MongoDbHealthCheck> logger)
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
            var client = new MongoClient(_settings.ConnectionString);
            var database = client.GetDatabase(_settings.DatabaseName);

            // Ping
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

            // Connection pool stats (MongoDB 7+)
            var serverStatus = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("serverStatus", 1),
                cancellationToken: cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "ping", "ok" }
            };

            string status;
            if (serverStatus.TryGetValue("connections", out var connDoc) &&
                connDoc is BsonDocument connections)
            {
                var current = connections["current"].AsInt32;
                var available = connections["available"].AsInt32;
                var total = current + available;
                var usagePct = total > 0 ? (double)current / total * 100 : 0;

                data["pool_current"] = current;
                data["pool_available"] = available;
                data["pool_usage_pct"] = Math.Round(usagePct, 1);

                if (usagePct >= 80)
                {
                    status = "MongoDB pool usage critical (" + Math.Round(usagePct, 1) + "%)";
                    _logger.LogWarning("MongoDB connection pool at {Pct}%", Math.Round(usagePct, 1));
                    return HealthCheckResult.Degraded(status, null, data);
                }
            }

            return HealthCheckResult.Healthy("MongoDB is reachable", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB health check failed");
            return HealthCheckResult.Unhealthy("MongoDB is not reachable", ex);
        }
    }
}
