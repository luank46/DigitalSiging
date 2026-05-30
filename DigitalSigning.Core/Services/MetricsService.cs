using System;
using System.Collections.Generic;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Implementation of <see cref="IMetricsService"/> that delegates to
    /// <see cref="PrometheusMetrics"/> for real metric reporting.
    /// </summary>
    public class MetricsService : IMetricsService
    {
        public void IncrementCounter(string name, string[]? labels = null)
        {
            switch (name)
            {
                case "transaction.created":
                    var provider = GetLabel(labels, 0) ?? "Unknown";
                    var tenant = GetLabel(labels, 1) ?? "Unknown";
                    PrometheusMetrics.RecordTransactionCreated(provider, tenant);
                    break;
                case "transaction.completed":
                    PrometheusMetrics.RecordTransactionCompleted(
                        GetLabel(labels, 0) ?? "Unknown",
                        GetLabel(labels, 1) ?? "Unknown",
                        0);
                    break;
                case "transaction.failed":
                    PrometheusMetrics.RecordTransactionFailed(
                        GetLabel(labels, 0) ?? "Unknown",
                        GetLabel(labels, 1) ?? "Unknown",
                        GetLabel(labels, 2) ?? "Unknown");
                    break;
                case "dlq.message":
                    PrometheusMetrics.RecordDlqMessage(GetLabel(labels, 0) ?? "Unknown");
                    break;
                default:
                    // Unknown counter — ignore silently
                    break;
            }
        }

        public void SetGauge(string name, double value, string[]? labels = null)
        {
            // Gauges are handled by MetricsCollector background service
        }

        public void RecordHistogram(string name, double value, string[]? labels = null)
        {
            switch (name)
            {
                case "step.duration":
                    if (Enum.TryParse<TransactionStep>(GetLabel(labels, 0), out var step))
                    {
                        PrometheusMetrics.RecordStepDuration(step, (long)value,
                            GetLabel(labels, 1) ?? "Unknown");
                    }
                    break;
                case "provider.latency":
                    PrometheusMetrics.RecordProviderLatency(
                        GetLabel(labels, 0) ?? "Unknown", (long)value);
                    break;
                default:
                    break;
            }
        }

        private static string? GetLabel(string[]? labels, int index)
        {
            if (labels == null || index >= labels.Length)
                return null;
            return labels[index];
        }
    }
}
