using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Interfaces
{
    /// <summary>
    /// Service for obtaining OAuth access tokens used when communicating with CA providers.
    /// </summary>
    public interface IOAuthService
    {
        /// <summary>Retrieves a valid OAuth bearer token.</summary>
        Task<string> GetAccessTokenAsync(CancellationToken ct = default);
    }
}
