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

namespace DigitalSigning.Core.Providers.Misa
{
    /// <summary>
    /// MISA-CA provider implementation.
    /// Flow: Login → GetCertificate → CreateHashData → SignHash → PollTransaction → AppendSignature
    ///
    /// MISA-CA (MISA Certification Authority) provides digital signing via REST API:
    ///   - Username/password authentication → JWT token
    ///   - Certificate management (list certificates by user)
    ///   - Hash signing (sync for basic, async for OTP/2FA flow)
    ///   - MISA SmartCA mobile app for OTP confirmation
    /// </summary>
    public class MisaProviderService : BaseProviderService, IProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MisaProviderService> _logger;
        private readonly ProviderSettings _settings;

        public MisaProviderService(
            HttpClient httpClient,
            IGridFsService gridFs,
            ITransactionService txService,
            ILogger<MisaProviderService> logger,
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
            _logger.LogInformation("MISA SignAsync: MaGiaoDich={MaGiaoDich}, IsWaiting={IsWaiting}",
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
                // 1. Login to MISA-CA
                var token = await LoginAsync(request, ct);
                if (string.IsNullOrEmpty(token))
                    return ErrorResult(request.MaGiaoDich, "Failed to login to MISA-CA");

                request.Token = token;

                // 2. Get certificate list and find matching one
                var certResult = await GetCertificateAsync(token, request, ct);
                if (certResult == null)
                    return ErrorResult(request.MaGiaoDich, "No matching certificate found in MISA-CA");

                request.Certificate = certResult.Certificate;
                request.CredentialId = certResult.CertId;
                request.SignatureAlgorithm = certResult.SignatureAlgorithm;
                request.SerialNumber = certResult.SerialNumber;

                // 3. Create hash data (from BaseProviderService)
                await CreateHashData(request, ct);

                // 4. Sign hash — MISA may return sync or async with OTP
                var signResult = await SignHashAsync(token, request, ct);
                if (signResult == null)
                    return ErrorResult(request.MaGiaoDich, "Failed to submit hash for MISA signing");

                if (signResult.IsWaiting)
                {
                    _logger.LogInformation("MISA requires OTP confirmation: TranId={TranId} for {MaGiaoDich}",
                        signResult.TransactionId, request.MaGiaoDich);

                    return new ProviderResult
                    {
                        MaGiaoDich = request.MaGiaoDich,
                        IsWaiting = true,
                        ProviderSessionId = signResult.TransactionId,
                        NextCheckAt = DateTime.UtcNow.AddSeconds(10),
                        DataHashes = request.DataHashes,
                        Hash = request.Hash,
                        Certificate = request.Certificate,
                    };
                }

                // Sync signature returned
                request.Signatures = signResult.Signatures;

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
                _logger.LogError(ex, "MISA SignAsync failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        private async Task<ProviderResult> HandleWaitingAsync(ProviderSignRequest request, CancellationToken ct)
        {
            try
            {
                string? token = request.Token;
                if (string.IsNullOrEmpty(token))
                {
                    token = await LoginAsync(request, ct);
                    if (string.IsNullOrEmpty(token))
                        return ErrorResult(request.MaGiaoDich, "Failed to re-login to MISA-CA");
                }

                var pollResult = await GetTransactionStatusAsync(token, request.ProviderSessionId, request, ct);

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
                        Certificate = request.Certificate,
                    };
                }

                if (pollResult.Signatures == null || pollResult.Signatures.Count == 0)
                    return ErrorResult(request.MaGiaoDich, "No signatures from MISA");

                request.Signatures = pollResult.Signatures;

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
                _logger.LogError(ex, "MISA HandleWaiting failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        // ── MISA API calls ──────────────────────────────────────────────

        /// <summary>
        /// Login to MISA-CA with username/password.
        /// Returns JWT bearer token.
        /// </summary>
        private async Task<string?> LoginAsync(ProviderSignRequest request, CancellationToken ct)
        {
            var payload = new
            {
                username = request.Username ?? "",
                password = request.Password ?? "",
                grantType = "password"
            };

            var response = await _httpClient.PostAsJsonAsync(_settings.MisaLoginUrl, payload, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // MISA returns token in various structures
            if (json.TryGetProperty("accessToken", out var accessEl))
                return accessEl.GetString();
            if (json.TryGetProperty("token", out var tokenEl))
                return tokenEl.GetString();

            // Nested in data object
            if (json.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.TryGetProperty("accessToken", out var dtAccess))
                    return dtAccess.GetString();
                if (dataEl.TryGetProperty("token", out var dtToken))
                    return dtToken.GetString();
            }

            return null;
        }

        /// <summary>
        /// Get certificate list and find the one matching the user/serial.
        /// </summary>
        private async Task<CertInfoResult?> GetCertificateAsync(string token,
            ProviderSignRequest request, CancellationToken ct)
        {
            var url = string.IsNullOrEmpty(request.SerialNumber)
                ? _settings.MisaCertListUrl
                : $"{_settings.MisaCertListUrl}?serialNumber={request.SerialNumber}";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Try to extract certificates from various response shapes
            JsonElement.ArrayEnumerator certsEnumerator = default;

            if (json.TryGetProperty("certificates", out var certs) && certs.ValueKind == JsonValueKind.Array)
                certsEnumerator = certs.EnumerateArray();
            else if (json.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.TryGetProperty("certificates", out var dc) && dc.ValueKind == JsonValueKind.Array)
                    certsEnumerator = dc.EnumerateArray();
                else if (dataEl.ValueKind == JsonValueKind.Array)
                    certsEnumerator = dataEl.EnumerateArray();
            }
            else if (json.ValueKind == JsonValueKind.Array)
            {
                certsEnumerator = json.EnumerateArray();
            }

            foreach (var c in certsEnumerator)
            {
                var certStr = c.TryGetProperty("certificate", out var certEl) ? certEl.GetString() : null;
                var certId = c.TryGetProperty("certId", out var idEl) ? idEl.GetString() : null;
                var serialStr = c.TryGetProperty("serialNumber", out var serEl) ? serEl.GetString() : null;
                var status = c.TryGetProperty("status", out var stEl) ? stEl.GetString() : "ACTIVE";

                // Skip inactive certificates
                if (status != "ACTIVE" && status != "VALID")
                    continue;

                // If serial is specified, match it
                if (!string.IsNullOrEmpty(request.SerialNumber) &&
                    !string.IsNullOrEmpty(serialStr) &&
                    !serialStr.Equals(request.SerialNumber, StringComparison.OrdinalIgnoreCase))
                    continue;

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
                        CertId = certId,
                        SerialNumber = serialStr,
                        SignatureAlgorithm = algo
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Submit hash for signing. MISA may require OTP via SmartCA app.
        /// </summary>
        private async Task<SignHashResult?> SignHashAsync(string token,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _settings.MisaSignUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                certId = request.CredentialId ?? "",
                serialNumber = request.SerialNumber ?? "",
                hashes = request.DataHashes ?? new List<string>(),
                hashAlgorithm = "SHA-256",
                signatureAlgorithm = request.SignatureAlgorithm ?? "1.2.840.113549.1.1.1",
                description = request.Notification ?? "Digital signing",
                responseType = "inline"
            });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Check for OTP/2FA requirement
            if (json.TryGetProperty("requireOtp", out var otpEl) && otpEl.GetBoolean())
            {
                var tranId = json.TryGetProperty("transactionId", out var tidEl)
                    ? tidEl.GetString()
                    : json.TryGetProperty("tranId", out var tid2) ? tid2.GetString() : null;

                return new SignHashResult
                {
                    IsWaiting = true,
                    TransactionId = tranId ?? Guid.NewGuid().ToString()
                };
            }

            // Check status for pending (async processing)
            if (json.TryGetProperty("status", out var statusEl))
            {
                var status = statusEl.GetString()?.ToUpperInvariant();
                if (status == "PENDING" || status == "PROCESSING")
                {
                    var tranId = json.TryGetProperty("transactionId", out var tid3)
                        ? tid3.GetString()
                        : Guid.NewGuid().ToString();
                    return new SignHashResult { IsWaiting = true, TransactionId = tranId };
                }
            }

            // Extract signatures
            var signatures = new List<string>();

            if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sigs.EnumerateArray())
                    signatures.Add(s.GetString() ?? "");
            }
            else if (json.TryGetProperty("signature", out var sigEl))
            {
                signatures.Add(sigEl.GetString() ?? "");
            }

            // Nested data
            if (signatures.Count == 0 && json.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.TryGetProperty("signatures", out var ds) && ds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in ds.EnumerateArray())
                        signatures.Add(s.GetString() ?? "");
                }
                else if (dataEl.TryGetProperty("signature", out var dSig))
                {
                    signatures.Add(dSig.GetString() ?? "");
                }
                else if (dataEl.TryGetProperty("transactionId", out var dTid))
                {
                    return new SignHashResult { IsWaiting = true, TransactionId = dTid.GetString() };
                }
            }

            return signatures.Count > 0
                ? new SignHashResult { Signatures = signatures }
                : null;
        }

        /// <summary>
        /// Poll transaction status for OTP flow.
        /// </summary>
        private async Task<PollResult> GetTransactionStatusAsync(string token, string? transactionId,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_settings.MisaTransactionUrl}/{transactionId}");
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
                case "OTP_REQUIRED":
                    return new PollResult { IsWaiting = true };

                case "COMPLETED":
                case "SUCCESS":
                case "APPROVED":
                {
                    var signatures = new List<string>();

                    if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in sigs.EnumerateArray())
                            signatures.Add(s.GetString() ?? "");
                    }
                    else if (json.TryGetProperty("signature", out var sigEl))
                    {
                        signatures.Add(sigEl.GetString() ?? "");
                    }

                    if (signatures.Count == 0 && json.TryGetProperty("data", out var dataEl2))
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
                case "CANCELLED":
                    _logger.LogWarning("MISA transaction {TransactionId} ended with status {Status}",
                        transactionId, status);
                    return new PollResult();

                default:
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
            public string? CertId { get; set; }
            public string? SerialNumber { get; set; }
            public string? SignatureAlgorithm { get; set; }
        }

        private class SignHashResult
        {
            public bool IsWaiting { get; set; }
            public string? TransactionId { get; set; }
            public List<string>? Signatures { get; set; }
        }

        private class PollResult
        {
            public bool IsWaiting { get; set; }
            public List<string>? Signatures { get; set; }
        }
    }
}
