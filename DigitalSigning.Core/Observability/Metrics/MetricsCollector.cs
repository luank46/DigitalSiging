using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Observability.Metrics;

/// <summary>
/// Background service that periodically collects and pushes internal metrics
/// (queue depths, in-flight counts, etc.) to <see cref="PrometheusMetrics"/>.
///
/// This decouples metric collection from the hot path — workers only signal
/// their state via cheap in-memory counters, and this collector flushes them
/// to Prometheus on a fixed interval.
/// </summary>
public class MetricsCollector : BackgroundService
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

    // ── Shared in-memory counters (populated by workers / services) ───

    /// <summary>Current number of transactions being processed (set by workers).</summary>
    public static long CurrentInFlight { get; set; }

    /// <summary>Approximate queue depth per queue name (set by consumers).</summary>
    public static readonly ConcurrentDictionary<string, long> QueueDepths = new();

    /// <summary>Approximate oldest message age in seconds per queue name.</summary>
    public static readonly ConcurrentDictionary<string, double> OldestMessageAgeSeconds = new();

    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricsCollector started — pushing gauges every {Interval}s",
            _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Collect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MetricsCollector iteration failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Pushes current in-memory values to Prometheus observable instruments.
    /// </summary>
    private void Collect()
    {
        // Future: push to ObservableGauge / UpDownCounter
        // For now this is a placeholder — real ObservableGauge callbacks
        // would be wired up in PrometheusMetrics via Meter.CreateObservableGauge
        // that reads from these static fields.

        _logger.LogDebug("MetricsCollector: InFlight={InFlight}, Queues={QueueCount}",
            CurrentInFlight, QueueDepths.Count);
    }
}
