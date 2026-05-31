using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Interfaces
{
    /// <summary>
    /// Downloads files from remote URLs to local storage for preparation.
    /// </summary>
    public interface IFileDownloader
    {
        /// <summary>Downloads a file and returns its local path.</summary>
        Task<string> DownloadAsync(string url, CancellationToken ct = default);
    }
}
