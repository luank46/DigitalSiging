using System;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Interface for basic metrics collection.
    /// </summary>
    public interface IMetricsService
    {
        /// <summary>
        /// Increment a counter with the given name and optional labels.
        /// </summary>
        void IncrementCounter(string name, string[]? labels = null);

        /// <summary>
        /// Record a gauge value.
        /// </summary>
        void SetGauge(string name, double value, string[]? labels = null);

        /// <summary>
        /// Record a histogram value.
        /// </summary>
        void RecordHistogram(string name, double value, string[]? labels = null);
    }
}