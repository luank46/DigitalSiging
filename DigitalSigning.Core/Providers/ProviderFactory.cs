using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Providers.Bkav;
using DigitalSigning.Core.Providers.Gcc;
using DigitalSigning.Core.Providers.Misa;
using DigitalSigning.Core.Providers.Viettel;
using DigitalSigning.Core.Providers.Vnpt;
using DigitalSigning.Core.Protection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Resolves IProviderService based on ProviderType or supplier name string.
    /// Tự động wrap mỗi provider với ProtectedProviderDecorator để thêm
    /// circuit breaker + rate limiter.
    /// </summary>
    public class ProviderFactory : IProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<ProviderType, IProviderService> _decoratedCache = new();

        public ProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IProviderService GetProvider(ProviderType providerType)
        {
            // Cache decorator để reuse circuit breaker state
            return _decoratedCache.GetOrAdd(providerType, CreateDecoratedProvider);
        }

        public IProviderService GetProvider(string supplierName)
        {
            var providerType = supplierName?.ToUpperInvariant() switch
            {
                SignHelper.NHA_PHAT_HANH_VNPT => ProviderType.Vnpt,
                SignHelper.NHA_PHAT_HANH_VIETTEL => ProviderType.Viettel,
                SignHelper.NHA_PHAT_HANH_BKAV => ProviderType.Bkav,
                SignHelper.NHA_PHAT_HANH_HSM => ProviderType.Gcc,
                SignHelper.NHA_PHAT_HANH_MISA => ProviderType.Misa,
                _ => throw new NotSupportedException($"Provider {supplierName} not yet implemented")
            };
            return GetProvider(providerType);
        }

        private IProviderService CreateDecoratedProvider(ProviderType providerType)
        {
            var inner = ResolveInner(providerType);
            var circuitBreaker = new CircuitBreaker(
                _serviceProvider.GetRequiredService<ILogger<CircuitBreaker>>(),
                failureThreshold: 5,
                openDuration: TimeSpan.FromSeconds(30),
                halfOpenSuccessThreshold: 3);
            var rateLimiter = _serviceProvider.GetRequiredService<RateLimiter>();
            var logger = _serviceProvider.GetRequiredService<ILogger<ProtectedProviderDecorator>>();

            return new ProtectedProviderDecorator(inner, circuitBreaker, rateLimiter, logger, providerType);
        }

        private IProviderService ResolveInner(ProviderType providerType)
        {
            return providerType switch
            {
                ProviderType.Vnpt => _serviceProvider.GetRequiredService<VnptProviderService>(),
                ProviderType.Viettel => _serviceProvider.GetRequiredService<ViettelProviderService>(),
                ProviderType.Bkav => _serviceProvider.GetRequiredService<BkavProviderService>(),
                ProviderType.Gcc => _serviceProvider.GetRequiredService<GccProviderService>(),
                ProviderType.Misa => _serviceProvider.GetRequiredService<MisaProviderService>(),
                _ => throw new NotSupportedException($"Provider type {providerType} not yet implemented")
            };
        }
    }
}
