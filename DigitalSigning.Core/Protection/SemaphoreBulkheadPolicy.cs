using System;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Simple bulkhead implementation that limits the number of concurrent executions
    /// of an asynchronous operation. Uses <see cref="SemaphoreSlim"/> under the hood.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by the wrapped operation.</typeparam>
    public class SemaphoreBulkheadPolicy<TResult> : IBulkheadPolicy<TResult>
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrency;

        /// <summary>
        /// Creates a bulkhead with the specified maximum concurrency.
        /// </summary>
        /// <param name="maxConcurrency">Maximum number of concurrent calls allowed (must be > 0).</param>
        public SemaphoreBulkheadPolicy(int maxConcurrency)
        {
            if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            _maxConcurrency = maxConcurrency;
            _semaphore      = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        /// <inheritdoc/>
        public async Task<TResult> ExecuteAsync(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default)
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Execute the wrapped operation while we hold the semaphore slot.
                return await operation(ct).ConfigureAwait(false);
            }
            finally
            {
                // Always release the slot – even if the operation throws.
                _semaphore.Release();
            }
        }
    }
}