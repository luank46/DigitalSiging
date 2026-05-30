using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Interface cho tất cả CA provider services (VNPT, Viettel, BKAV, GCC, MISA).
    /// Mỗi provider implement SignAsync theo flow:
    ///   GetToken → GetCredential → GetCertInfo → CreateHashData → Authorize/Sign → AppendSignature
    /// </summary>
    public interface IProviderService
    {
        /// <summary>
        /// Thực hiện luồng ký với CA provider.
        /// </summary>
        /// <param name="request">Thông tin request (credentials, hash data, file refs).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Kết quả từ provider.</returns>
        Task<ProviderResult> SignAsync(ProviderSignRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// Factory để resolve provider service theo ProviderType.
    /// </summary>
    public interface IProviderFactory
    {
        IProviderService GetProvider(ProviderType providerType);
        IProviderService GetProvider(string supplierName);
    }
}
