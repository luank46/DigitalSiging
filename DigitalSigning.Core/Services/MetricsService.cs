using System;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<string, Prometheus.Counter.Child> _counterCache = new();

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
                    // Generic counter for API-level metrics:
                    // api_rate_limited_total, api_validation_errors,
                    // api_file_lock_conflicts_total, transactions_created_total,
                    // api_errors_total, api_not_found_total
                    {
                        var labelValues = labels != null
                            ? Array.ConvertAll(labels, SanitizeMetricLabel)
                            : new[] { "default" };
                        var cacheKey = $"{name}/{string.Join(",", labelValues)}";
                        var child = _counterCache.GetOrAdd(cacheKey, _ =>
                            Prometheus.Metrics.CreateCounter(name, name,
                                new Prometheus.CounterConfiguration
                                {
                                    LabelNames = labels?.Length > 0
                                        ? labels.Select((_, i) => $"label_{i}").ToArray()
                                        : new[] { "label" }
                                }).WithLabels(labelValues));
                        child.Inc();
                    }
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
            return SanitizeMetricLabel(labels[index]);
        }

        /// <summary>
        /// Sanitize a metric label value to prevent Prometheus metric explosions
        /// and ensure compatibility with monitoring systems.
        /// Removes characters that are unsafe or commonly cause issues:
        ///   - dots, slashes, backslashes, semicolons, quotes
        ///   - newlines and control characters
        ///   - leading/trailing whitespace
        /// Preserves colon ':' as it is commonly used as key:value separator in labels.
        /// Truncates to a maximum length to prevent label bloat.
        /// </summary>
        private static string SanitizeMetricLabel(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "Unknown";

            // Remove characters unsafe for metric systems:
            // - < 0x20 (control chars), 0x7F (DEL)
            // - . ; / \ | " ' < > [ ] { } ( ) & % $ # @ ! ~ ` ^ = + ?
            // Preserve : (colon) as it is used as key:value separator
            var sanitized = new System.Text.StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c is >= (char)0x20 and not (char)0x7F &&
                    c is not ('.' or ';' or '/' or '\\' or '|' or '"' or '\'' or
                    '<' or '>' or '[' or ']' or '{' or '}' or '(' or ')' or
                    '&' or '%' or '$' or '#' or '@' or '!' or '~' or '`' or
                    '^' or '=' or '+' or '?' or ',' or '*'))
                {
                    sanitized.Append(c);
                }
                else
                {
                    sanitized.Append('_');
                }
            }

            var result = sanitized.ToString().Trim();

            // Truncate to 100 chars to prevent label bloat
            if (result.Length > 100)
                result = result[..100];

            return string.IsNullOrWhiteSpace(result) ? "Unknown" : result;
        }
    }
}
