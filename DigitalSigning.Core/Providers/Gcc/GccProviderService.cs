using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Services.Pdf;
using DigitalSigning.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalSigning.Core.Providers.Gcc
{
    /// <summary>
    /// GCC (Ban Cơ Yếu / HSM) provider implementation.
    /// Flow: GetToken → GetCertificate → CreateHashData → SignHash → Poll → AppendSignature
    ///
    /// GCC uses a REST API that wraps HSM (Hardware Security Module) signing:
    ///   - Token-based authentication (access key / secret)
    ///   - HSM stores private keys internally — only hash data is sent for signing
    ///   - Supports both synchronous and asynchronous signing
    ///   - Certificate download for chain verification
    /// </summary>
    public class GccProviderService : BaseProviderService, IProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GccProviderService> _logger;
        private readonly ProviderSettings _settings;

        public GccProviderService(
            HttpClient httpClient,
            IGridFsService gridFs,
            ITransactionService txService,
            ILogger<GccProviderService> logger,
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
            _logger.LogInformation("GCC SignAsync: MaGiaoDich={MaGiaoDich}, IsWaiting={IsWaiting}",
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
                // 1. Get API token using access key / secret
                var token = await GetTokenAsync(request, ct);
                if (string.IsNullOrEmpty(token))
                    return ErrorResult(request.MaGiaoDich, "Failed to get GCC token");

                request.Token = token;

                // 2. Get certificate from HSM
                var certResult = await GetCertificateAsync(token, request, ct);
                if (certResult == null)
                    return ErrorResult(request.MaGiaoDich, "Failed to get certificate from GCC HSM");

                request.Certificate = certResult.Certificate;
                request.CredentialId = certResult.KeyId;
                request.SignatureAlgorithm = certResult.SignatureAlgorithm;

                // 3. Create hash data (from BaseProviderService)
                await CreateHashData(request, ct);

                // 4. Submit hash for HSM signing
                var signResult = await SignHashAsync(token, request, ct);
                if (string.IsNullOrEmpty(signResult))
                    return ErrorResult(request.MaGiaoDich, "Failed to submit hash for GCC HSM signing");

                // Check if result is a transaction ID (async) or completed inline
                if (signResult == "PENDING" || signResult == "WAITING")
                {
                    // GCC returns transaction ID separately
                    var tranId = request.ProviderSessionId ?? Guid.NewGuid().ToString();

                    _logger.LogInformation("GCC signing is pending: TranId={TranId} for {MaGiaoDich}",
                        tranId, request.MaGiaoDich);

                    return new ProviderResult
                    {
                        MaGiaoDich = request.MaGiaoDich,
                        IsWaiting = true,
                        ProviderSessionId = tranId,
                        NextCheckAt = DateTime.UtcNow.AddSeconds(5),
                        DataHashes = request.DataHashes,
                        Hash = request.Hash,
                        Certificate = request.Certificate,
                    };
                }

                // Inline signature returned as base64 string (transaction ID format)
                request.Signatures = new List<string> { signResult };

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
                _logger.LogError(ex, "GCC SignAsync failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        private async Task<ProviderResult> HandleWaitingAsync(ProviderSignRequest request, CancellationToken ct)
        {
            try
            {
                // Restore token
                string? token = request.Token;
                if (string.IsNullOrEmpty(token))
                {
                    token = await GetTokenAsync(request, ct);
                    if (string.IsNullOrEmpty(token))
                        return ErrorResult(request.MaGiaoDich, "Failed to re-authenticate GCC");
                }

                // Poll transaction status
                var pollResult = await GetTransactionStatusAsync(token, request.ProviderSessionId, request, ct);

                if (pollResult.IsWaiting)
                {
                    return new ProviderResult
                    {
                        MaGiaoDich = request.MaGiaoDich,
                        IsWaiting = true,
                        ProviderSessionId = request.ProviderSessionId,
                        NextCheckAt = DateTime.UtcNow.AddSeconds(5),
                        DataHashes = request.DataHashes,
                        Hash = request.Hash,
                        Certificate = request.Certificate,
                    };
                }

                if (pollResult.Signatures == null || pollResult.Signatures.Count == 0)
                    return ErrorResult(request.MaGiaoDich, "No signatures from GCC");

                request.Signatures = pollResult.Signatures;

                // Append signatures
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
                _logger.LogError(ex, "GCC HandleWaiting failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        // ── GCC API calls ───────────────────────────────────────────────

        /// <summary>
        /// Get authentication token from GCC HSM API.
        /// GCC uses API key + secret (or certificate-based auth) rather than OAuth2.
        /// </summary>
        private async Task<string?> GetTokenAsync(ProviderSignRequest request, CancellationToken ct)
        {
            // GCC typically uses either:
            //   1. API key + API secret (in headers or body)
            //   2. Username + password with grant_type
            var payload = new
            {
                username = request.Username ?? "",
                password = request.Password ?? "",
                grant_type = "password",
                scope = "sign"
            };

            // Add API key headers if present — per-request
            var req = new HttpRequestMessage(HttpMethod.Post, _settings.GccTokenUrl);
            req.Content = JsonContent.Create(payload);
            if (!string.IsNullOrEmpty(request.SerialNumber))
            {
                req.Headers.Add("X-API-Key", request.SerialNumber);
            }

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (json.TryGetProperty("accessToken", out var accessEl))
                return accessEl.GetString();
            if (json.TryGetProperty("access_token", out var accessTokenEl))
                return accessTokenEl.GetString();
            if (json.TryGetProperty("token", out var tokenEl))
                return tokenEl.GetString();
            if (json.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("token", out var dataTokenEl))
                return dataTokenEl.GetString();

            return null;
        }

        /// <summary>
        /// Get certificate associated with the signing key in HSM.
        /// </summary>
        private async Task<CertInfoResult?> GetCertificateAsync(string token,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _settings.GccCertUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                keyId = request.CredentialId ?? "",
                serialNumber = request.SerialNumber ?? "",
                includeChain = true
            });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // GCC returns certificate in various formats
            string? certStr = null;
            string? keyId = null;

            if (json.TryGetProperty("certificate", out var certEl))
                certStr = certEl.GetString();
            else if (json.TryGetProperty("cert", out var cEl))
                certStr = cEl.GetString();
            else if (json.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.TryGetProperty("certificate", out var dcEl))
                    certStr = dcEl.GetString();
                if (dataEl.TryGetProperty("keyId", out var kidEl))
                    keyId = kidEl.GetString();
            }

            if (json.TryGetProperty("keyId", out var keyIdEl))
                keyId = keyIdEl.GetString();

            if (!string.IsNullOrEmpty(certStr))
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(certStr); }
                catch { bytes = System.Text.Encoding.UTF8.GetBytes(certStr); }

                var algo = "1.2.840.113549.1.1.1"; // default RSA
                try
                {
                    using var x509 = new System.Security.Cryptography.X509Certificates.X509Certificate2(bytes);
                    algo = x509.PublicKey.Oid.FriendlyName == "RSA"
                        ? "1.2.840.113549.1.1.1"
                        : "1.2.840.10045.4.3.2";
                }
                catch { /* use default */ }

                return new CertInfoResult
                {
                    Certificate = bytes,
                    KeyId = keyId,
                    SignatureAlgorithm = algo
                };
            }

            return null;
        }

        /// <summary>
        /// Submit hash data for HSM signing.
        /// GCC HSM signs the hash internally and returns the signature.
        /// </summary>
        private async Task<string?> SignHashAsync(string token,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _settings.GccSignUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                keyId = request.CredentialId ?? "",
                hashes = request.DataHashes ?? new List<string>(),
                hashAlgorithm = "SHA-256",
                signatureAlgorithm = request.SignatureAlgorithm ?? "1.2.840.113549.1.1.1",
                description = request.Notification ?? "Digital signing",
                responseType = "inline" // request inline response if possible
            });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // GCC may return:
            //   1. Direct signature string
            //   2. Array of signatures
            //   3. Transaction ID for async processing
            //   4. PENDING status (async HSM processing)

            // Check for status first
            if (json.TryGetProperty("status", out var statusEl))
            {
                var status = statusEl.GetString()?.ToUpperInvariant();
                if (status == "PENDING" || status == "WAITING" || status == "PROCESSING")
                {
                    // Store session ID for polling
                    if (json.TryGetProperty("transactionId", out var tidEl))
                        request.ProviderSessionId = tidEl.GetString();
                    return status;
                }

                if (status == "FAILED" || status == "REJECTED")
                {
                    var errMsg = json.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : status;
                    _logger.LogWarning("GCC signing failed: {Error}", errMsg);
                    return null;
                }
            }

            // Direct signature in various formats
            if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sigs.EnumerateArray())
                    return s.GetString(); // return first signature
            }

            if (json.TryGetProperty("signature", out var sigEl))
                return sigEl.GetString();

            // Nested data
            if (json.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.TryGetProperty("signatures", out var ds) && ds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in ds.EnumerateArray())
                        return s.GetString();
                }
                if (dataEl.TryGetProperty("signature", out var dSig))
                    return dSig.GetString();
                if (dataEl.TryGetProperty("transactionId", out var dTid))
                {
                    request.ProviderSessionId = dTid.GetString();
                    return "PENDING";
                }
            }

            // Transaction ID means we need to poll
            if (json.TryGetProperty("transactionId", out var tid2El))
            {
                request.ProviderSessionId = tid2El.GetString();
                return "PENDING";
            }

            return null;
        }

        /// <summary>
        /// Poll transaction status for async HSM signing.
        /// </summary>
        private async Task<PollResult> GetTransactionStatusAsync(string token, string? transactionId,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_settings.GccTransactionUrl}/{transactionId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Extract status from various formats
            string? status = null;

            if (json.TryGetProperty("status", out var statusEl))
                status = statusEl.GetString();
            else if (json.TryGetProperty("tranStatus", out var tsEl))
                status = tsEl.GetString();
            else if (json.TryGetProperty("data", out var dataEl) &&
                     dataEl.TryGetProperty("status", out var dsEl))
                status = dsEl.GetString();

            switch (status?.ToUpperInvariant())
            {
                case "PENDING":
                case "WAITING":
                case "PROCESSING":
                case "IN_PROGRESS":
                    return new PollResult { IsWaiting = true };

                case "COMPLETED":
                case "SUCCESS":
                case "DONE":
                {
                    var signatures = new List<string>();

                    // Extract signatures from various response structures
                    if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in sigs.EnumerateArray())
                            signatures.Add(s.GetString() ?? "");
                    }
                    else if (json.TryGetProperty("signature", out var sigEl))
                    {
                        signatures.Add(sigEl.GetString() ?? "");
                    }
                    else if (json.TryGetProperty("data", out var dataEl2))
                    {
                        if (dataEl2.TryGetProperty("signatures", out var ds) && ds.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var s in ds.EnumerateArray())
                                signatures.Add(s.GetString() ?? "");
                        }
                        else if (dataEl2.TryGetProperty("signature", out var dSig))
                        {
                            signatures.Add(dSig.GetString() ?? "");
                        }
                    }

                    return new PollResult
                    {
                        Signatures = signatures.Count > 0 ? signatures : null
                    };
                }

                case "FAILED":
                case "REJECTED":
                case "TIMEOUT":
                case "ERROR":
                    _logger.LogWarning("GCC transaction {TransactionId} ended with status {Status}",
                        transactionId, status);
                    return new PollResult();

                default:
                    // Unknown — keep waiting
                    return new PollResult { IsWaiting = true };
            }
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
            public string? KeyId { get; set; }
            public string? SignatureAlgorithm { get; set; }
        }

        private class PollResult
        {
            public bool IsWaiting { get; set; }
            public List<string>? Signatures { get; set; }
        }
    }
}
