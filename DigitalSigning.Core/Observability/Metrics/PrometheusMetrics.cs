using System.Diagnostics.Metrics;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Observability.Tracing;

namespace DigitalSigning.Core.Observability.Metrics;

/// <summary>
/// Central registry of all Prometheus / OpenTelemetry metrics used by the system.
/// Provides strongly-typed instruments so callers don't manipulate raw metric names.
///
/// Design:
/// - Uses the built-in <see cref="Meter"/> from System.Diagnostics.Metrics,
///   which is automatically exported by OpenTelemetry and prometheus-net.
/// - Every instrument is static (singleton) — this is the recommended pattern
///   for .NET metrics, matching how the runtime exposes its own counters.
/// - Tags (dimensions) are attached via the `tag` parameter on each record call.
/// </summary>
public static class PrometheusMetrics
{
    public static readonly Meter Meter = new("DigitalSigning.Metrics", "1.0.0");

    /// <summary>Total transactions created, tagged by provider and tenant.</summary>
    private static readonly Counter<long> TransactionsCreated = Meter.CreateCounter<long>(
        "signing.transactions.created.total",
        description: "Total number of transactions created");

    /// <summary>Transactions that completed successfully.</summary>
    private static readonly Counter<long> TransactionsCompleted = Meter.CreateCounter<long>(
        "signing.transactions.completed.total",
        description: "Total number of transactions completed successfully");

    /// <summary>Transactions that failed (any reason).</summary>
    private static readonly Counter<long> TransactionsFailed = Meter.CreateCounter<long>(
        "signing.transactions.failed.total",
        description: "Total number of transactions that failed");

    /// <summary>Transactions that timed out while waiting for user confirmation.</summary>
    private static readonly Counter<long> TransactionsTimeout = Meter.CreateCounter<long>(
        "signing.transactions.timeout.total",
        description: "Total number of transactions that timed out");

    /// <summary>Messages sent to the dead-letter queue.</summary>
    private static readonly Counter<long> DlqMessages = Meter.CreateCounter<long>(
        "signing.dlq.messages.total",
        description: "Messages moved to the dead-letter queue");

    /// <summary>Rate-limited requests (rejected by rate limiter).</summary>
    private static readonly Counter<long> RateLimitedRequests = Meter.CreateCounter<long>(
        "signing.rate_limit.rejected.total",
        description: "Requests rejected by rate limiter");

    // ── Histograms ────────────────────────────────────────────────────

    /// <summary>End-to-end transaction duration in milliseconds.</summary>
    private static readonly Histogram<double> TransactionDuration = Meter.CreateHistogram<double>(
        "signing.transaction.duration_ms",
        description: "Transaction processing duration",
        unit: "ms");

    /// <summary>Duration of individual steps (FilePrepare, Hash, Provider, etc.).</summary>
    private static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>(
        "signing.step.duration_ms",
        description: "Step processing duration",
        unit: "ms");

    /// <summary>Latency of calls to external CA providers.</summary>
    private static readonly Histogram<double> ProviderLatency = Meter.CreateHistogram<double>(
        "signing.provider.latency_ms",
        description: "CA provider API call latency",
        unit: "ms");

    // ── Gauges ────────────────────────────────────────────────────────

    /// <summary>Number of transactions currently in-flight (not yet terminal).</summary>
    private static readonly ObservableGauge<long> TransactionsInFlight = Meter.CreateObservableGauge(
        "signing.transactions.in_flight",
        () => new Measurement<long>(0), // Placeholder — real value pushed by MetricsCollector
        description: "Number of transactions currently being processed");

    // ── Public record helpers ──────────────────────────────────────────

    public static void RecordTransactionCreated(string provider, string tenantId)
        => TransactionsCreated.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public static void RecordTransactionCompleted(string provider, string tenantId, long elapsedMs)
    {
        TransactionsCompleted.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("tenant_id", tenantId));
        TransactionDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("provider", provider));
    }

    public static void RecordTransactionFailed(string provider, string tenantId, string errorCode)
        => TransactionsFailed.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("error_code", errorCode));

    public static void RecordTransactionTimeout(string provider, string tenantId)
        => TransactionsTimeout.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public static void RecordStepDuration(TransactionStep step, long elapsedMs, string provider)
        => StepDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("step", step.ToString()),
            new KeyValuePair<string, object?>("provider", provider));

    public static void RecordProviderLatency(string provider, long elapsedMs)
        => ProviderLatency.Record(elapsedMs,
            new KeyValuePair<string, object?>("provider", provider));

    public static void RecordDlqMessage(string provider)
        => DlqMessages.Add(1,
            new KeyValuePair<string, object?>("provider", provider));

    public static void RecordRateLimited(string policy, string key)
        => RateLimitedRequests.Add(1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("key", key));
}
