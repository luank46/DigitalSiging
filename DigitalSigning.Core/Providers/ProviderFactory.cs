using System;
using System.Collections.Generic;
using System.Linq;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Providers.Bkav;
using DigitalSigning.Core.Providers.Gcc;
using DigitalSigning.Core.Providers.Misa;
using DigitalSigning.Core.Providers.Viettel;
using DigitalSigning.Core.Providers.Vnpt;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Resolves IProviderService based on ProviderType or supplier name string.
    /// </summary>
    public class ProviderFactory : IProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public ProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IProviderService GetProvider(ProviderType providerType)
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

        public IProviderService GetProvider(string supplierName)
        {
            return supplierName?.ToUpperInvariant() switch
            {
                SignHelper.NHA_PHAT_HANH_VNPT => _serviceProvider.GetRequiredService<VnptProviderService>(),
                SignHelper.NHA_PHAT_HANH_VIETTEL => _serviceProvider.GetRequiredService<ViettelProviderService>(),
                SignHelper.NHA_PHAT_HANH_BKAV => _serviceProvider.GetRequiredService<BkavProviderService>(),
                SignHelper.NHA_PHAT_HANH_HSM => _serviceProvider.GetRequiredService<GccProviderService>(),
                SignHelper.NHA_PHAT_HANH_MISA => _serviceProvider.GetRequiredService<MisaProviderService>(),
                _ => throw new NotSupportedException($"Provider {supplierName} not yet implemented")
            };
        }
    }
}
