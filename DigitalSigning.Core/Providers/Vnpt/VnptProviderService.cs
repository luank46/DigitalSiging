using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Services.Pdf;
using DigitalSigning.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalSigning.Core.Providers.Vnpt
{
    /// <summary>
    /// VNPT SmartCA provider implementation.
    /// Flow: GetToken → GetCredential → GetCertInfo → CreateHashData → SignHash → Poll → AppendSignature
    ///
    /// URLs được cấu hình qua IOptions&lt;ProviderSettings&gt; (appsettings.json section "Providers").
    /// </summary>
    public class VnptProviderService : BaseProviderService, IProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VnptProviderService> _logger;
        private readonly ProviderSettings _settings;

        public VnptProviderService(
            HttpClient httpClient,
            IGridFsService gridFs,
            ITransactionService txService,
            ILogger<VnptProviderService> logger,
            IPdfSigningService pdfSigning,
            IOptions<ProviderSettings> settings)
            : base(gridFs, txService, logger, pdfSigning)
        {
            _httpClient = httpClient;
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task<ProviderResult> SignAsync(ProviderSignRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation("VNPT SignAsync: MaGiaoDich={MaGiaoDich}, IsWaiting={IsWaiting}",
                request.MaGiaoDich, request.IsWaiting);

            if (request.IsWaiting)
            {
                return await HandleWaitingAsync(request, ct);
            }

            return await HandleFirstCallAsync(request, ct);
        }

        private async Task<ProviderResult> HandleFirstCallAsync(ProviderSignRequest request, CancellationToken ct)
        {
            try
            {
                // 1. Get OAuth token
                var token = await GetTokenAsync(request, ct);
                if (string.IsNullOrEmpty(token))
                    return ErrorResult(request.MaGiaoDich, "Failed to get VNPT token");

                // 2. Get credential ID
                var credentialId = await GetCredentialAsync(token, request, ct);
                if (string.IsNullOrEmpty(credentialId))
                    return ErrorResult(request.MaGiaoDich, "No matching credential found");

                request.CredentialId = credentialId;

                // 3. Get certificate info
                var certResult = await GetCertInfoAsync(token, credentialId, request, ct);
                if (certResult == null)
                    return ErrorResult(request.MaGiaoDich, "Failed to get certificate info");

                request.Certificate = certResult.Certificate;
                request.SignatureAlgorithm = certResult.SignatureAlgorithm;

                // 4. Create hash data
                await CreateHashData(request, ct);

                // 5. Sign hash - get transaction ID
                var signResult = await SignHashAsync(token, credentialId, request, ct);
                if (string.IsNullOrEmpty(signResult))
                    return ErrorResult(request.MaGiaoDich, "Failed to submit hash for signing");

                _logger.LogInformation("VNPT SignHash submitted: TranId={TranId} for {MaGiaoDich}",
                    signResult, request.MaGiaoDich);

                return new ProviderResult
                {
                    MaGiaoDich = request.MaGiaoDich,
                    IsWaiting = true,
                    ProviderSessionId = signResult,
                    NextCheckAt = DateTime.UtcNow.AddSeconds(10),
                    DataHashes = request.DataHashes,
                    Hash = request.Hash,
                    Certificate = request.Certificate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VNPT SignAsync failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        private async Task<ProviderResult> HandleWaitingAsync(ProviderSignRequest request, CancellationToken ct)
        {
            try
            {
                // Poll for transaction result
                var token = await GetTokenAsync(request, ct);
                var pollResult = await GetTransactionAsync(token, request.ProviderSessionId, request, ct);

                if (pollResult.IsWaiting)
                {
                    return new ProviderResult
                    {
                        MaGiaoDich = request.MaGiaoDich,
                        IsWaiting = true,
                        ProviderSessionId = request.ProviderSessionId,
                        NextCheckAt = DateTime.UtcNow.AddSeconds(10),
                        DataHashes = request.DataHashes,
                        Hash = request.Hash,
                        Certificate = request.Certificate
                    };
                }

                if (pollResult.Signatures == null || pollResult.Signatures.Count == 0)
                    return ErrorResult(request.MaGiaoDich, "No signatures from VNPT");

                request.Signatures = pollResult.Signatures;

                // Append signatures to documents
                var appendResult = await AppendSignature(request, ct);
                if (!appendResult.Success)
                    return ErrorResult(request.MaGiaoDich, appendResult.ErrorMessage ?? "Append failed");

                return new ProviderResult
                {
                    MaGiaoDich = request.MaGiaoDich,
                    IsWaiting = false,
                    Signatures = request.Signatures,
                    SignedFiles = appendResult.SignedFiles,
                    DataHashes = request.DataHashes,
                    Hash = request.Hash,
                    Certificate = request.Certificate,
                    MaTrangThaiKy = SignHelper.MA_TRANG_THAI_DA_KY
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VNPT HandleWaiting failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        // ── VNPT API calls ──────────────────────────────────────────────

        private async Task<string?> GetTokenAsync(ProviderSignRequest request, CancellationToken ct)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _settings.VnptClientId),
                new KeyValuePair<string, string>("client_secret", _settings.VnptClientSecret),
                new KeyValuePair<string, string>("username", request.Username ?? ""),
                new KeyValuePair<string, string>("password", request.Password ?? ""),
                new KeyValuePair<string, string>("grant_type", "password")
            });

            var response = await _httpClient.PostAsync(_settings.VnptTokenUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return json.TryGetProperty("access_token", out var token) ? token.GetString() : null;
        }

        private async Task<string?> GetCredentialAsync(string token, ProviderSignRequest request, CancellationToken ct)
        {
            var requestMsg = new HttpRequestMessage(HttpMethod.Post, _settings.VnptCredentialListUrl);
            requestMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            requestMsg.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(requestMsg, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("credentialIDs", out var ids) && ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                    return id.GetString();
            }
            return null;
        }

        private async Task<CertInfoResult?> GetCertInfoAsync(string token, string credentialId,
            ProviderSignRequest request, CancellationToken ct)
        {
            var requestMsg = new HttpRequestMessage(HttpMethod.Post, _settings.VnptCredentialInfoUrl);
            requestMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            requestMsg.Content = JsonContent.Create(new { credentialId, certificates = "single", certInfo = true, authInfo = true });

            var response = await _httpClient.SendAsync(requestMsg, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("cert", out var certEl) &&
                certEl.TryGetProperty("certificates", out var certs) &&
                certs.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in certs.EnumerateArray())
                {
                    var certStr = c.GetString();
                    if (!string.IsNullOrEmpty(certStr))
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(certStr);
                        // For VNPT, cert comes as base64
                        try { bytes = Convert.FromBase64String(certStr); } catch { }
                        return new CertInfoResult
                        {
                            Certificate = bytes,
                            SignatureAlgorithm = "1.2.840.113549.1.1.1"
                        };
                    }
                }
            }
            return null;
        }

        private async Task<string?> SignHashAsync(string token, string credentialId,
            ProviderSignRequest request, CancellationToken ct)
        {
            var tranId = Guid.NewGuid().ToString();
            var payload = new
            {
                credentialId,
                refTranId = tranId,
                description = request.Notification ?? "Digital signing",
                datas = request.DataHashes ?? new List<string>()
            };

            var requestMsg = new HttpRequestMessage(HttpMethod.Post, _settings.VnptSignHashUrl);
            requestMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            requestMsg.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(requestMsg, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("code", out var code) && code.GetInt32() == 0)
            {
                return json.TryGetProperty("tranId", out var tid) ? tid.GetString() : tranId;
            }
            return null;
        }

        private async Task<PollResult> GetTransactionAsync(string token, string? sessionId,
            ProviderSignRequest request, CancellationToken ct)
        {
            var requestMsg = new HttpRequestMessage(HttpMethod.Post, _settings.VnptTransactionInfoUrl);
            requestMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            requestMsg.Content = JsonContent.Create(new { tranId = sessionId ?? "" });

            var response = await _httpClient.SendAsync(requestMsg, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Check status
            if (json.TryGetProperty("tranStatus", out var status))
            {
                var statusVal = status.GetInt32();
                if (statusVal == 4000) // Still waiting
                    return new PollResult { IsWaiting = true };

                if (statusVal == 1 && json.TryGetProperty("signatures", out var sigs))
                {
                    var signatures = new List<string>();
                    foreach (var s in sigs.EnumerateArray())
                        signatures.Add(s.GetString() ?? "");
                    return new PollResult { Signatures = signatures };
                }
            }

            return new PollResult();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static ProviderResult ErrorResult(string maGiaoDich, string error)
        {
            return new ProviderResult
            {
                MaGiaoDich = maGiaoDich,
                IsWaiting = false,
                ErrorMessage = error,
                ErrorCode = Enums.ErrorCode.UnexpectedException,
                MaTrangThaiKy = SignHelper.MA_TRANG_THAI_KY_KHONG_THANH_CONG
            };
        }

        private class CertInfoResult
        {
            public byte[]? Certificate { get; set; }
            public string? SignatureAlgorithm { get; set; }
        }

        private class PollResult
        {
            public bool IsWaiting { get; set; }
            public List<string>? Signatures { get; set; }
        }
    }
}
