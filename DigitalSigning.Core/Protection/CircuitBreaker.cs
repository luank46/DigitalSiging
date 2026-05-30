using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Represents the possible states of a circuit breaker.
    /// </summary>
    public enum CircuitState
    {
        Closed,   // Normal operation – calls pass through.
        Open,     // Calls are rejected immediately.
        HalfOpen // A limited number of test calls are allowed.
    }

    /// <summary>
    /// Interface for a circuit‑breaker. The break‑point is called via <c>ExecuteAsync</c>,
    /// which returns <c>true</c> if the operation was allowed (and succeeded), or <c>false</c>
    /// if the circuit was open and the call was blocked.
    /// </summary>
    public interface ICircuitBreaker
    {
        Task<bool> ExecuteAsync(Func<Task> operation, CancellationToken ct = default);
        CircuitState State { get; }
    }

    /// <summary>
    /// Stateful circuit‑breaker implementation with configurable thresholds, open‑duration, and
    /// half‑open success requirement.
    /// </summary>
    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly int _failureThreshold;           // failures required to open
        private readonly TimeSpan _openDuration;        // how long to stay open
        private readonly int _halfOpenSuccessThreshold; // successes needed to close
        private readonly ILogger _log;

        private int _failureCount;
        private int _successCountDuringHalfOpen;
        private DateTime _openedAt;
        private CircuitState _state = CircuitState.Closed;
        private readonly object _sync = new();

        public CircuitBreaker(
            ILogger<CircuitBreaker> logger,
            int failureThreshold = 5,
            TimeSpan? openDuration = null,
            int halfOpenSuccessThreshold = 3)
        {
            _log = logger;
            _failureThreshold          = failureThreshold;
            _openDuration              = openDuration ?? TimeSpan.FromSeconds(30);
            _halfOpenSuccessThreshold  = halfOpenSuccessThreshold;
        }

        public CircuitState State
        {
            get
            {
                lock (_sync)
                {
                    // Transition from Open → HalfOpen when the open period has elapsed
                    if (_state == CircuitState.Open && DateTime.UtcNow - _openedAt >= _openDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        _successCountDuringHalfOpen = 0;
                        _log?.LogInformation("CircuitBreaker moved to HalfOpen after open duration elapsed.");
                    }
                    return _state;
                }
            }
        }

        public async Task<bool> ExecuteAsync(Func<Task> operation, CancellationToken ct = default)
        {
            // Fast‑path – reject if circuit is open
            if (State == CircuitState.Open)
            {
                _log?.LogWarning("CircuitBreaker is OPEN – request rejected.");
                return false;
            }

            try
            {
                await operation();
                OnSuccess();
                return true;
            }
            catch (Exception)
            {
                OnFailure();
                // Re‑throwing is optional; callers decide what to do on false.
                return false;
            }
        }

        private void OnSuccess()
        {
            lock (_sync)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    _successCountDuringHalfOpen++;
                    if (_successCountDuringHalfOpen >= _halfOpenSuccessThreshold)
                    {
                        // Sufficient successes – close the circuit.
                        _state = CircuitState.Closed;
                        _failureCount = 0;
                        _log?.LogInformation("CircuitBreaker CLOSED after successful half‑open trials.");
                    }
                }
                else if (_state == CircuitState.Closed)
                {
                    // Reset failure counter on any success while closed.
                    _failureCount = 0;
                }
            }
        }

        private void OnFailure()
        {
            lock (_sync)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    // Any failure during half‑open forces an immediate reopen.
                    OpenCircuit();
                }
                else if (_state == CircuitState.Closed)
                {
                    _failureCount++;
                    if (_failureCount >= _failureThreshold)
                    {
                        OpenCircuit();
                    }
                }
            }
        }

        private void OpenCircuit()
        {
            _state    = CircuitState.Open;
            _openedAt = DateTime.UtcNow;
            _log?.LogWarning($"CircuitBreaker OPENED after {_failureCount} consecutive failures.");
        }
    }
}