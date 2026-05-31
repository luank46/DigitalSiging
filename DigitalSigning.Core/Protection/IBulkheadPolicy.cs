using System;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Bulkhead (concurrency‑limiting) policy. Implementations restrict the number of
    /// concurrent executions of the wrapped operation. A typical implementation uses a
    /// <see cref="SemaphoreSlim"/> to gate entry.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by the operation.</typeparam>
    public interface IBulkheadPolicy<TResult>
    {
        /// <summary>
        /// Executes <paramref name="operation"/> while respecting the configured concurrency limit.
        /// If the limit is reached the call will wait (asynchronously) until a slot becomes free.
        /// </summary>
        Task<TResult> ExecuteAsync(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default);
    }
}