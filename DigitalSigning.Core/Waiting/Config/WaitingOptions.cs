using System;

namespace DigitalSigning.Core.Waiting.Config
{
    /// <summary>
    /// Configuration options for the Waiting module.
    /// </summary>
    public class WaitingOptions
    {
        /// <summary>
        /// Redis Sorted Set key used to store waiting transactions.
        /// </summary>
        public string SortedSetKey { get; set; } = "waiting:signatures";

        /// <summary>
        /// Interval in seconds between polling cycles.
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of expired items to process per polling cycle.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Default interval in seconds before the next check for a still-waiting transaction.
        /// </summary>
        public int NextCheckIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum time a transaction can stay in WaitingUserConfirm status before timeout (seconds).
        /// Default: 330 seconds (matching the legacy 330s retry window).
        /// </summary>
        public int WaitingTimeoutSeconds { get; set; } = 330;

        /// <summary>
        /// TTL for the Redis distributed lock (seconds). Should be longer than a single poll cycle.
        /// </summary>
        public int LockTtlSeconds { get; set; } = 60;

        /// <summary>
        /// Redis lock key prefix.
        /// </summary>
        public string LockKeyPrefix { get; set; } = "lock:waiting:";
    }
}
