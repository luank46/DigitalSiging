using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalSigning.Core.Services.Monitoring
{
    /// <summary>
    /// InfluxDB monitoring service — ghi metrics xuống InfluxDB v2 bằng HTTP API.
    /// Giữ nguyên logic từ InfluxDbService cũ, dùng HttpClient thay vì InfluxDB.Client.
    /// Lỗi InfluxDB không ảnh hưởng đến luồng ký chính.
    /// </summary>
    public class InfluxDbService : IMonitoringService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<InfluxDbService> _logger;
        private readonly InfluxSettings _settings;

        public InfluxDbService(
            IOptions<InfluxSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<InfluxDbService> logger)
            : this(settings.Value, httpClientFactory.CreateClient(), logger)
        {
        }

        public InfluxDbService(
            InfluxSettings settings,
            HttpClient httpClient,
            ILogger<InfluxDbService> logger)
        {
            _settings = settings;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Ghi metric dạng line protocol: measurement,tag=value field=value timestamp
        /// Ví dụ: chu_ky_dien_tu,action=sign_hash value=150 1234567890
        /// </summary>
        public async Task WriteTimeMetric(string metrics, string action, long time)
        {
            try
            {
                var line = $"{metrics},action={action} value={time}";
                await WriteLineProtocolAsync(line);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InfluxDB write failed (non-critical)");
            }
        }

        /// <summary>
        /// Ghi danh sách metric dạng line protocol.
        /// </summary>
        public async Task WriteTimeMetricByString(List<string> metrics)
        {
            try
            {
                foreach (var line in metrics)
                {
                    await WriteLineProtocolAsync(line);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InfluxDB batch write failed (non-critical)");
            }
        }

        /// <summary>
        /// Ghi dữ liệu thô.
        /// </summary>
        public void Write(string data)
        {
            // Fire-and-forget — legacy compatibility
            Task.Run(async () =>
            {
                try
                {
                    await WriteLineProtocolAsync(data);
                }
                catch { /* non-critical */ }
            });
        }

        private async Task WriteLineProtocolAsync(string line)
        {
            if (string.IsNullOrEmpty(_settings.Host) || string.IsNullOrEmpty(_settings.Token))
                return;

            var url = $"{_settings.Host.TrimEnd('/')}/api/v2/write" +
                      $"?org={Uri.EscapeDataString(_settings.Org)}" +
                      $"&bucket={Uri.EscapeDataString(_settings.Bucket)}" +
                      "&precision=ns";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Token {_settings.Token}");
            request.Content = new StringContent(line, Encoding.UTF8);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// InfluxDB connection settings.
    /// </summary>
    public class InfluxSettings
    {
        public string Host { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Org { get; set; } = string.Empty;
        public string Bucket { get; set; } = string.Empty;
    }
}
