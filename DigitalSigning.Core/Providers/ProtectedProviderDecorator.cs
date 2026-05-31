using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Protection;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Decorator pattern — wraps IProviderService với CircuitBreaker + RateLimiter.
    ///
    /// Flow:
    ///   1. RateLimiter.TryAcquire(provider, tenant) — check quota
    ///   2. CircuitBreaker.State — check circuit (Open → reject nhanh)
    ///   3. Execute SignAsync → success/failure → cập nhật counters
    ///
    /// Mỗi provider có circuit breaker riêng (keyed by ProviderType).
    /// Rate limit theo provider + tenant.
    /// </summary>
    public class ProtectedProviderDecorator : IProviderService
    {
        private readonly IProviderService _inner;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly RateLimiter _rateLimiter;
        private readonly ILogger<ProtectedProviderDecorator> _logger;
        private readonly ProviderType _providerType;

        public ProtectedProviderDecorator(
            IProviderService inner,
            ICircuitBreaker circuitBreaker,
            RateLimiter rateLimiter,
            ILogger<ProtectedProviderDecorator> logger,
            ProviderType providerType)
        {
            _inner = inner;
            _circuitBreaker = circuitBreaker;
            _rateLimiter = rateLimiter;
            _logger = logger;
            _providerType = providerType;
        }

        public async Task<ProviderResult> SignAsync(ProviderSignRequest request, CancellationToken ct = default)
        {
            // Step 1: Rate limit check
            if (!_rateLimiter.TryAcquire(_providerType, request.TenantId))
            {
                _logger.LogWarning(
                    "Rate limit exceeded for provider={Provider} tenant={Tenant} tx={Tx}",
                    _providerType, request.TenantId, request.MaGiaoDich);

                PrometheusMetrics.RecordRateLimited("provider", _providerType.ToString());

                return new ProviderResult
                {
                    MaGiaoDich = request.MaGiaoDich,
                    IsWaiting = false,
                    ErrorMessage = "Rate limit exceeded. Please try again later.",
                    ErrorCode = ErrorCode.UnexpectedException,
                    MaTrangThaiKy = SignHelper.MA_TRANG_THAI_KY_KHONG_THANH_CONG
                };
            }

            // Step 2: Circuit breaker check (fast reject)
            var circuitResult = false;
            ProviderResult? result = null;

            try
            {
                circuitResult = await _circuitBreaker.ExecuteAsync(
                    async () =>
                    {
                        result = await _inner.SignAsync(request, ct);

                        // Provider lỗi → coi như failure để circuit breaker count
                        if (result != null && result.ErrorCode.HasValue)
                        {
                            throw new ProviderSignException(result.ErrorMessage ?? "Provider error");
                        }
                    },
                    ct);

                if (!circuitResult)
                {
                    // Circuit is open
                    _logger.LogWarning(
                        "Circuit breaker OPEN for provider={Provider} tx={Tx}",
                        _providerType, request.MaGiaoDich);

                    return new ProviderResult
                    {
                        MaGiaoDich = request.MaGiaoDich,
                        IsWaiting = false,
                        ErrorMessage = $"Provider {_providerType} is temporarily unavailable. Circuit breaker open.",
                        ErrorCode = ErrorCode.UnexpectedException,
                        MaTrangThaiKy = SignHelper.MA_TRANG_THAI_KY_KHONG_THANH_CONG
                    };
                }

                return result!;
            }
            catch (ProviderSignException)
            {
                // Error already handled by circuit breaker + rate limiter
                return result ?? new ProviderResult
                {
                    MaGiaoDich = request.MaGiaoDich,
                    IsWaiting = false,
                    ErrorMessage = "Provider signing failed",
                    ErrorCode = ErrorCode.UnexpectedException,
                    MaTrangThaiKy = SignHelper.MA_TRANG_THAI_KY_KHONG_THANH_CONG
                };
            }
        }
    }

    /// <summary>
    /// Exception dùng để signal circuit breaker khi provider trả về lỗi business logic.
    /// </summary>
    public class ProviderSignException : Exception
    {
        public ProviderSignException(string message) : base(message) { }
    }
}
