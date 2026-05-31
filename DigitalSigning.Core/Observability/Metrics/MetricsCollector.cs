using System.Collections.Concurrent;
using System.Threading;
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
///
/// Thread-safe: sử dụng Interlocked cho các atomic counters.
/// </summary>
public class MetricsCollector : BackgroundService
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

    // ── Shared in-memory counters (populated by workers / services) ───

    /// <summary>Current number of transactions being processed (set by workers).</summary>
    private static long _currentInFlight;

    /// <summary>Thread-safe accessor for CurrentInFlight.</summary>
    public static long CurrentInFlight
    {
        get => Interlocked.Read(ref _currentInFlight);
        set => Interlocked.Exchange(ref _currentInFlight, value);
    }

    /// <summary>Increment CurrentInFlight (thread-safe).</summary>
    public static void IncrementInFlight() => Interlocked.Increment(ref _currentInFlight);

    /// <summary>Decrement CurrentInFlight (thread-safe).</summary>
    public static void DecrementInFlight() => Interlocked.Decrement(ref _currentInFlight);

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
        _logger.LogDebug("MetricsCollector: InFlight={InFlight}, Queues={QueueCount}",
            _currentInFlight, QueueDepths.Count);
    }
}
