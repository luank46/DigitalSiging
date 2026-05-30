using System;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Interfaces
{
    /// <summary>
    /// Generic retry policy that wraps an async operation with configurable backoff logic.
    /// </summary>
    public interface IRetryPolicy<TResult>
    {
        /// <summary>Executes the operation with retry logic.</summary>
        Task<TResult> ExecuteAsync(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default);
    }
}
