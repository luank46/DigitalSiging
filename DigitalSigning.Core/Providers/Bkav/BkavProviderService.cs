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

namespace DigitalSigning.Core.Providers.Bkav
{
    /// <summary>
    /// BKAV CA provider implementation.
    /// Flow: Login → GetCertificate → CreateHashData → SignHash → PollTransaction → AppendSignature
    ///
    /// BKAV CA uses a simpler REST API compared to CSC standard:
    ///   - Login with username/password → session token
    ///   - Get certificate list (filter by serial if provided)
    ///   - Sign hash (sync or async with transaction polling)
    ///   - OTP verification via BKAV SmartCA app
    /// </summary>
    public class BkavProviderService : BaseProviderService, IProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BkavProviderService> _logger;
        private readonly ProviderSettings _settings;

        public BkavProviderService(
            HttpClient httpClient,
            IGridFsService gridFs,
            ITransactionService txService,
            ILogger<BkavProviderService> logger,
            IPdfSigningService pdfSigning,
            IOptions<ProviderSettings> settings)
            : base(gridFs, txService, logger, pdfSigning)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<ProviderResult> SignAsync(ProviderSignRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation("BKAV SignAsync: MaGiaoDich={MaGiaoDich}, IsWaiting={IsWaiting}",
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
                // 1. Login to BKAV CA
                var token = await LoginAsync(request, ct);
                if (string.IsNullOrEmpty(token))
                    return ErrorResult(request.MaGiaoDich, "Failed to login to BKAV CA");

                request.Token = token;

                // 2. Get certificate info
                var certResult = await GetCertificateAsync(token, request, ct);
                if (certResult == null)
                    return ErrorResult(request.MaGiaoDich, "No matching certificate found");

                request.Certificate = certResult.Certificate;
                request.CredentialId = certResult.CertId;
                request.SignatureAlgorithm = certResult.SignatureAlgorithm;

                // 3. Create hash data (from BaseProviderService)
                await CreateHashData(request, ct);

                // 4. Sign hash
                var signResult = await SignHashAsync(token, request, ct);
                if (signResult == null)
                    return ErrorResult(request.MaGiaoDich, "Failed to submit hash for signing");

                // BKAV may return immediate signature or transaction ID for OTP flow
                if (signResult.IsWaiting)
                {
                    _logger.LogInformation("BKAV SignHash requires OTP confirmation: TranId={TranId} for {MaGiaoDich}",
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

                // Signatures returned directly (no OTP flow)
                request.Signatures = signResult.Signatures;

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
                _logger.LogError(ex, "BKAV SignAsync failed for {MaGiaoDich}", request.MaGiaoDich);
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
                    token = await LoginAsync(request, ct);
                    if (string.IsNullOrEmpty(token))
                        return ErrorResult(request.MaGiaoDich, "Failed to re-login to BKAV CA");
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
                        NextCheckAt = DateTime.UtcNow.AddSeconds(10),
                        DataHashes = request.DataHashes,
                        Hash = request.Hash,
                        Certificate = request.Certificate,
                    };
                }

                if (pollResult.Signatures == null || pollResult.Signatures.Count == 0)
                    return ErrorResult(request.MaGiaoDich, "No signatures from BKAV");

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
                _logger.LogError(ex, "BKAV HandleWaiting failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        // ── BKAV API calls ──────────────────────────────────────────────

        /// <summary>
        /// Login to BKAV CA with username/password.
        /// Returns a session token (JWT or bearer token).
        /// </summary>
        private async Task<string?> LoginAsync(ProviderSignRequest request, CancellationToken ct)
        {
            var payload = new
            {
                username = request.Username ?? "",
                password = request.Password ?? ""
            };

            var response = await _httpClient.PostAsJsonAsync(_settings.BkavLoginUrl, payload, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // BKAV may return token in different fields
            if (json.TryGetProperty("token", out var tokenEl))
                return tokenEl.GetString();
            if (json.TryGetProperty("accessToken", out var accessTokenEl))
                return accessTokenEl.GetString();
            if (json.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("token", out var dataToken))
                return dataToken.GetString();

            return null;
        }

        /// <summary>
        /// Get certificate list and find matching certificate.
        /// </summary>
        private async Task<CertInfoResult?> GetCertificateAsync(string token,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _settings.BkavCertListUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { serialNumber = request.SerialNumber, status = "ACTIVE" });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Try to extract certificates array from response structure
            JsonElement.ArrayEnumerator certsEnumerator = default;

            if (json.TryGetProperty("certificates", out var certs) && certs.ValueKind == JsonValueKind.Array)
                certsEnumerator = certs.EnumerateArray();
            else if (json.TryGetProperty("data", out var data) &&
                     data.TryGetProperty("certificates", out var dataCerts) &&
                     dataCerts.ValueKind == JsonValueKind.Array)
                certsEnumerator = dataCerts.EnumerateArray();

            // ReSharper disable once GenericEnumeratorNotDisposed — JsonElement.ArrayEnumerator is a ref struct
            foreach (var c in certsEnumerator)
            {
                var certId = c.TryGetProperty("certId", out var idEl) ? idEl.GetString() : null;
                var certStr = c.TryGetProperty("certificate", out var certEl) ? certEl.GetString() : null;
                var serialStr = c.TryGetProperty("serialNumber", out var serEl) ? serEl.GetString() : null;

                // If serialNumber is specified, match it
                if (!string.IsNullOrEmpty(request.SerialNumber) &&
                    !string.IsNullOrEmpty(serialStr) &&
                    !serialStr.Equals(request.SerialNumber, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(certStr))
                {
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(certStr); }
                    catch { bytes = System.Text.Encoding.UTF8.GetBytes(certStr); }

                    // Determine algorithm
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
                        SignatureAlgorithm = algo
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Submit hash for signing.
        /// BKAV may return signature directly (sync) or transaction ID for OTP flow (async).
        /// </summary>
        private async Task<SignHashResult?> SignHashAsync(string token,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _settings.BkavSignUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                certId = request.CredentialId,
                hashes = request.DataHashes ?? new List<string>(),
                hashAlgorithm = "SHA-256",
                description = request.Notification ?? "Digital signing",
                signAlgo = request.SignatureAlgorithm ?? "1.2.840.113549.1.1.1"
            });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // BKAV returns "requireOtp: true" when user needs to confirm on mobile
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

            // Direct signature response
            if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
            {
                var signatures = new List<string>();
                foreach (var s in sigs.EnumerateArray())
                    signatures.Add(s.GetString() ?? "");

                if (signatures.Count > 0)
                    return new SignHashResult { Signatures = signatures };
            }

            // Single signature field
            if (json.TryGetProperty("signature", out var sigEl))
            {
                return new SignHashResult
                {
                    Signatures = new List<string> { sigEl.GetString() ?? "" }
                };
            }

            // Nested data structure
            if (json.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.TryGetProperty("signatures", out var dataSigs) && dataSigs.ValueKind == JsonValueKind.Array)
                {
                    var signatures = new List<string>();
                    foreach (var s in dataSigs.EnumerateArray())
                        signatures.Add(s.GetString() ?? "");
                    if (signatures.Count > 0)
                        return new SignHashResult { Signatures = signatures };
                }
                if (dataEl.TryGetProperty("signature", out var dataSig))
                    return new SignHashResult { Signatures = new List<string> { dataSig.GetString() ?? "" } };
            }

            return null;
        }

        /// <summary>
        /// Poll transaction status for OTP flow.
        /// </summary>
        private async Task<PollResult> GetTransactionStatusAsync(string token, string? transactionId,
            ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.BkavTransactionUrl}/{transactionId}/status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { transactionId = transactionId ?? "" });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Check status via various response formats
            string? status = null;

            if (json.TryGetProperty("status", out var statusEl))
                status = statusEl.GetString();
            else if (json.TryGetProperty("tranStatus", out var tranStatusEl))
                status = tranStatusEl.GetString();
            else if (json.TryGetProperty("data", out var dataEl) &&
                     dataEl.TryGetProperty("status", out var dataStatusEl))
                status = dataStatusEl.GetString();

            switch (status?.ToUpperInvariant())
            {
                case "PENDING":
                case "WAITING":
                case "PROCESSING":
                    return new PollResult { IsWaiting = true };

                case "COMPLETED":
                case "SUCCESS":
                case "APPROVED":
                    // Extract signatures from various possible response structures
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

                    return new PollResult { Signatures = signatures.Count > 0 ? signatures : null };

                case "FAILED":
                case "REJECTED":
                case "TIMEOUT":
                case "EXPIRED":
                    _logger.LogWarning("BKAV transaction {TransactionId} ended with status {Status}",
                        transactionId, status);
                    return new PollResult();

                default:
                    // Unknown status — keep waiting
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
