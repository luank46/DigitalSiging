using global::MongoDB.Bson;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.HealthChecks;

/// <summary>
/// Health check that verifies MongoDB connectivity by running a ping command.
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
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("MongoDB is reachable");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB health check failed");
            return HealthCheckResult.Unhealthy("MongoDB is not reachable", ex);
        }
    }
}
