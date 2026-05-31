using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Service for storing/retrieving file bytes in MongoDB GridFS.
    /// </summary>
    public interface IGridFsService
    {
        /// <summary>Uploads file bytes to GridFS and returns the file ID.</summary>
        Task<string> UploadFileAsync(byte[] fileBytes, string fileName, CancellationToken ct = default);

        /// <summary>Downloads file bytes from GridFS by file ID.</summary>
        Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);

        /// <summary>Deletes a file from GridFS by file ID.</summary>
        Task<bool> DeleteFileAsync(string fileId, CancellationToken ct = default);

        /// <summary>Checks if MongoDB GridFS is reachable.</summary>
        Task<bool> IsHealthyAsync(CancellationToken ct = default);
    }
}
