using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Downloads files from remote URLs with retry logic.
    /// Matching legacy Worker.GetByteFileFromUrlV2 pattern: 3 retries, 2s delay between.
    /// </summary>
    public class FileDownloader : IFileDownloader
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FileDownloader> _logger;

        private const int MaxRetries = 3;
        private const int RetryDelayMs = 2000;

        public FileDownloader(IHttpClientFactory httpClientFactory, ILogger<FileDownloader> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<string> DownloadAsync(string url, CancellationToken ct = default)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Downloading file from {Url}, attempt {Attempt}/{MaxRetries}",
                        url, attempt, MaxRetries);

                    var response = await client.GetAsync(url, ct);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using var stream = await response.Content.ReadAsStreamAsync(ct);
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream, ct);
                        var bytes = memoryStream.ToArray();

                        if (bytes.Length > 0)
                        {
                            // Save to temp file and return path
                            var tempDir = Path.Combine(Path.GetTempPath(), "signing_files");
                            Directory.CreateDirectory(tempDir);
                            var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{Path.GetFileName(url) ?? "file.tmp"}");
                            await File.WriteAllBytesAsync(tempFile, bytes, ct);

                            _logger.LogInformation("Downloaded file {FileName} ({Size} bytes) from {Url}",
                                Path.GetFileName(tempFile), bytes.Length, url);
                            return tempFile;
                        }

                        _logger.LogWarning("Downloaded empty file from {Url}, attempt {Attempt}", url, attempt);
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        _logger.LogWarning("Server error (500) downloading from {Url}, attempt {Attempt}", url, attempt);
                        if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
                        continue;
                    }
                    else
                    {
                        _logger.LogWarning("HTTP {StatusCode} downloading from {Url}, attempt {Attempt}",
                            response.StatusCode, url, attempt);
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "HTTP error downloading from {Url}, attempt {Attempt}/{MaxRetries}",
                        url, attempt, MaxRetries);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "Timeout downloading from {Url}, attempt {Attempt}/{MaxRetries}",
                        url, attempt, MaxRetries);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error downloading from {Url}, attempt {Attempt}/{MaxRetries}",
                        url, attempt, MaxRetries);
                }

                if (attempt < MaxRetries)
                    await Task.Delay(RetryDelayMs, ct);
            }

            throw new InvalidOperationException(
                $"Failed to download file from {url} after {MaxRetries} attempts");
        }
    }
}
