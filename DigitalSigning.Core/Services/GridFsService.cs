using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Implementation of GridFS file storage for signed/unsigned file bytes.
    /// Matches the legacy MongoDbService pattern from the old system.
    /// </summary>
    public class GridFsService : IGridFsService
    {
        private readonly IGridFSBucket _bucket;
        private readonly ILogger<GridFsService> _logger;

        public GridFsService(MongoDbContext context, ILogger<GridFsService> logger)
        {
            var database = context.Database;
            _bucket = new GridFSBucket(database, new GridFSBucketOptions
            {
                BucketName = "signing_files",
                ChunkSizeBytes = 255 * 1024, // 255 KB chunks
            });
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> UploadFileAsync(byte[] fileBytes, string fileName, CancellationToken ct = default)
        {
            try
            {
                var options = new GridFSUploadOptions
                {
                    Metadata = new BsonDocument
                    {
                        { "fileName", fileName },
                        { "uploadedAt", DateTime.UtcNow }
                    }
                };

                using var stream = new MemoryStream(fileBytes);
                var fileId = await _bucket.UploadFromStreamAsync(fileName, stream, options, ct);

                _logger.LogDebug("Uploaded file {FileName} to GridFS with ID {FileId}", fileName, fileId);
                return fileId.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file {FileName} to GridFS", fileName);
                throw;
            }
        }

        public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
        {
            try
            {
                if (!ObjectId.TryParse(fileId, out var objectId))
                {
                    throw new ArgumentException($"Invalid GridFS file ID: {fileId}");
                }

                using var stream = new MemoryStream();
                await _bucket.DownloadToStreamAsync(objectId, stream, cancellationToken: ct);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download file {FileId} from GridFS", fileId);
                throw;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileId, CancellationToken ct = default)
        {
            try
            {
                if (!ObjectId.TryParse(fileId, out var objectId))
                {
                    _logger.LogWarning("Invalid GridFS file ID for deletion: {FileId}", fileId);
                    return false;
                }

                await _bucket.DeleteAsync(objectId, ct);
                _logger.LogDebug("Deleted file {FileId} from GridFS", fileId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file {FileId} from GridFS", fileId);
                return false;
            }
        }

        public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
        {
            try
            {
                // GridFS uses the same connection as MongoDB, so we just ping the DB.
                var db = _bucket.Database;
                await db.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: ct);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
