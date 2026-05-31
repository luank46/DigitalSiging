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

namespace DigitalSigning.Core.Providers.Viettel
{
    /// <summary>
    /// Viettel CA provider implementation.
    /// Flow: GetToken → Authorize (SAD) → GetCredential → CreateHashData → SignHash → Poll → AppendSignature
    ///
    /// Viettel uses Cloud Signature Consortium (CSC) standard v1.0.4.0.
    /// URLs được cấu hình qua IOptions&lt;ProviderSettings&gt;.
    /// Per-request Authorization headers — không dùng DefaultRequestHeaders.
    /// </summary>
    public class ViettelProviderService : BaseProviderService, IProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly IViettelAuthStateService _authStateService;
        private readonly ILogger<ViettelProviderService> _logger;
        private readonly ProviderSettings _settings;

        public ViettelProviderService(
            HttpClient httpClient,
            IGridFsService gridFs,
            ITransactionService txService,
            IViettelAuthStateService authStateService,
            ILogger<ViettelProviderService> logger,
            IPdfSigningService pdfSigning,
            IOptions<ProviderSettings> settings)
            : base(gridFs, txService, logger, pdfSigning)
        {
            _httpClient = httpClient;
            _authStateService = authStateService;
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task<ProviderResult> SignAsync(ProviderSignRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation("Viettel SignAsync: MaGiaoDich={MaGiaoDich}, IsWaiting={IsWaiting}",
                request.MaGiaoDich, request.IsWaiting);

            if (request.IsWaiting)
                return await HandleWaitingAsync(request, ct);
            return await HandleFirstCallAsync(request, ct);
        }

        private async Task<ProviderResult> HandleFirstCallAsync(ProviderSignRequest request, CancellationToken ct)
        {
            try
            {
                var token = await GetTokenAsync(request, ct);
                if (string.IsNullOrEmpty(token))
                    return ErrorResult(request.MaGiaoDich, "Failed to get Viettel OAuth token");

                var sad = await AuthenticateSADAsync(token, request, ct);
                if (string.IsNullOrEmpty(sad))
                    return ErrorResult(request.MaGiaoDich, "Failed to get SAD from Viettel authorization");
                request.Sad = sad;

                var credentialId = await GetCredentialAsync(token, sad, request, ct);
                if (string.IsNullOrEmpty(credentialId))
                    return ErrorResult(request.MaGiaoDich, "No matching credential found");
                request.CredentialId = credentialId;

                var certResult = await GetCertInfoAsync(token, credentialId, request, ct);
                if (certResult == null)
                    return ErrorResult(request.MaGiaoDich, "Failed to get certificate info");
                request.Certificate = certResult.Certificate;
                request.SignatureAlgorithm = certResult.SignatureAlgorithm;

                await CreateHashData(request, ct);

                var signResult = await SignHashAsync(token, credentialId, sad, request, ct);
                if (string.IsNullOrEmpty(signResult))
                    return ErrorResult(request.MaGiaoDich, "Failed to submit Viettel signing request");

                _logger.LogInformation("Viettel SignHash submitted: bgTask={BgTask} for {MaGiaoDich}", signResult, request.MaGiaoDich);
                await _authStateService.SaveAuthStateAsync(request.MaGiaoDich, token, sad, request.SerialNumber, ct);

                return new ProviderResult
                {
                    MaGiaoDich = request.MaGiaoDich, IsWaiting = true, ProviderSessionId = signResult,
                    NextCheckAt = DateTime.UtcNow.AddSeconds(5), Sad = sad,
                    DataHashes = request.DataHashes, Hash = request.Hash, Certificate = request.Certificate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Viettel SignAsync failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        private async Task<ProviderResult> HandleWaitingAsync(ProviderSignRequest request, CancellationToken ct)
        {
            try
            {
                var authState = await _authStateService.GetAuthStateAsync(request.MaGiaoDich, ct);
                if (authState == null)
                    return ErrorResult(request.MaGiaoDich, "Viettel auth state not found; cannot poll");

                string? token = authState.Token;
                if (string.IsNullOrEmpty(token))
                    token = await GetTokenAsync(request, ct);
                string? sad = authState.Sad;
                if (string.IsNullOrEmpty(sad))
                    sad = await AuthenticateSADAsync(token!, request, ct);
                if (string.IsNullOrEmpty(sad))
                    return ErrorResult(request.MaGiaoDich, "Failed to refresh Viettel SAD");

                var pollResult = await GetBgTaskStatusAsync(token!, request.ProviderSessionId, request, ct);
                if (pollResult.IsWaiting)
                {
                    await _authStateService.ExtendTransactionLockAsync(request.MaGiaoDich, ct);
                    return new ProviderResult
                    {
                        MaGiaoDich = request.MaGiaoDich, IsWaiting = true, ProviderSessionId = request.ProviderSessionId,
                        NextCheckAt = DateTime.UtcNow.AddSeconds(5), Sad = sad,
                        DataHashes = request.DataHashes, Hash = request.Hash, Certificate = request.Certificate
                    };
                }

                if (pollResult.Signatures == null || pollResult.Signatures.Count == 0)
                    return ErrorResult(request.MaGiaoDich, "No signatures from Viettel");

                request.Signatures = pollResult.Signatures;
                await _authStateService.ClearAuthStateAsync(request.MaGiaoDich, ct);

                var appendResult = await AppendSignature(request, ct);
                if (!appendResult.Success)
                    return ErrorResult(request.MaGiaoDich, appendResult.ErrorMessage ?? "Append failed");

                return new ProviderResult
                {
                    MaGiaoDich = request.MaGiaoDich, IsWaiting = false, Signatures = request.Signatures,
                    SignedFiles = appendResult.SignedFiles, DataHashes = request.DataHashes,
                    Hash = request.Hash, Certificate = request.Certificate, Sad = sad,
                    MaTrangThaiKy = SignHelper.MA_TRANG_THAI_DA_KY
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Viettel HandleWaiting failed for {MaGiaoDich}", request.MaGiaoDich);
                return ErrorResult(request.MaGiaoDich, ex.Message);
            }
        }

        // ── Viettel API calls (per-request Authorization headers) ────────

        private async Task<string?> GetTokenAsync(ProviderSignRequest request, CancellationToken ct)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _settings.ViettelClientId),
                new KeyValuePair<string, string>("client_secret", _settings.ViettelClientSecret),
                new KeyValuePair<string, string>("username", request.Username ?? ""),
                new KeyValuePair<string, string>("password", request.Password ?? ""),
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("scope", "openid")
            });

            var response = await _httpClient.PostAsync(_settings.ViettelTokenUrl, content, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return json.TryGetProperty("access_token", out var token) ? token.GetString() : null;
        }

        private async Task<string?> AuthenticateSADAsync(string token, ProviderSignRequest request, CancellationToken ct)
        {
            var payload = new
            {
                credentialIDs = string.IsNullOrEmpty(request.CredentialId) ? null : new[] { request.CredentialId },
                numSignatures = request.Files?.Count ?? 1,
                consentDescription = request.Notification ?? "Sign document",
                responseType = "code",
                redirectUri = "https://example.com/callback"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ViettelCscBaseUrl}authorize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("SAD", out var sadEl)) return sadEl.GetString();

            if (json.TryGetProperty("authorizationCode", out var authCodeEl))
            {
                var authCode = authCodeEl.GetString();
                if (!string.IsNullOrEmpty(authCode))
                    return await ExchangeCodeForSADAsync(token, authCode, ct);
            }
            return null;
        }

        private async Task<string?> ExchangeCodeForSADAsync(string token, string authCode, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ViettelCscBaseUrl}authorize/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { authorizationCode = authCode, grantType = "authorization_code" });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return json.TryGetProperty("SAD", out var sadEl) ? sadEl.GetString() : null;
        }

        private async Task<string?> GetCredentialAsync(string token, string sad, ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ViettelCscBaseUrl}credentials/list");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { SAD = sad, serialNumber = request.SerialNumber });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("credentialIDs", out var ids) && ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                    if (!string.IsNullOrEmpty(id.GetString())) return id.GetString();
            }
            return null;
        }

        private async Task<CertInfoResult?> GetCertInfoAsync(string token, string credentialId, ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ViettelCscBaseUrl}credentials/info");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { credentialId, authInfo = true, certInfo = true, certificates = "chain" });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (json.TryGetProperty("cert", out var certEl) &&
                certEl.TryGetProperty("certificates", out var certs) &&
                certs.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in certs.EnumerateArray())
                {
                    var certStr = c.GetString();
                    if (string.IsNullOrEmpty(certStr)) continue;

                    byte[] bytes;
                    if (certStr.Contains("-----BEGIN"))
                    {
                        var pemContent = certStr.Replace("-----BEGIN CERTIFICATE-----", "")
                            .Replace("-----END CERTIFICATE-----", "").Replace("\n", "").Replace("\r", "");
                        bytes = Convert.FromBase64String(pemContent.Trim());
                    }
                    else
                    {
                        bytes = Convert.FromBase64String(certStr);
                    }

                    using var x509 = new System.Security.Cryptography.X509Certificates.X509Certificate2(bytes);
                    var algo = x509.PublicKey.Oid.FriendlyName == "RSA"
                        ? "1.2.840.113549.1.1.1" : "1.2.840.10045.4.3.2";
                    return new CertInfoResult { Certificate = bytes, SignatureAlgorithm = algo };
                }
            }
            return null;
        }

        private async Task<string?> SignHashAsync(string token, string credentialId, string sad, ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ViettelCscBaseUrl}signatures/signHash");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                credentialId, SAD = sad, signatureType = "foreground",
                signAlgo = request.SignatureAlgorithm ?? "1.2.840.113549.1.1.1",
                hashAlgo = "2.16.840.1.101.3.4.2.1",
                hashes = request.DataHashes ?? new List<string>(),
                description = request.Notification ?? "Digital signing"
            });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (json.TryGetProperty("bgTaskID", out var bgEl)) return bgEl.GetString();

            if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
            {
                var sigList = new List<string>();
                foreach (var s in sigs.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String) sigList.Add(s.GetString() ?? "");
                if (sigList.Count > 0) { request.Signatures = sigList; return "completed-inline"; }
            }
            return null;
        }

        private async Task<PollResult> GetBgTaskStatusAsync(string token, string? bgTaskId, ProviderSignRequest request, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ViettelCscBaseUrl}bgTasks/{bgTaskId}/status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { bgTaskID = bgTaskId ?? "" });

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (json.TryGetProperty("status", out var status))
            {
                var statusStr = status.GetString();
                if (statusStr == "WAITING" || statusStr == "PENDING")
                    return new PollResult { IsWaiting = true };
                if (statusStr == "COMPLETED")
                    return await GetTaskSignaturesAsync(token, bgTaskId, ct);
                _logger.LogWarning("Viettel bgTask {BgTaskId} ended with status {Status}", bgTaskId, statusStr);
            }
            return new PollResult();
        }

        private async Task<PollResult> GetTaskSignaturesAsync(string token, string? bgTaskId, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{_settings.ViettelCscBaseUrl}bgTasks/{bgTaskId}/result");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (json.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
            {
                var sigList = new List<string>();
                foreach (var s in sigs.EnumerateArray()) sigList.Add(s.GetString() ?? "");
                return new PollResult { Signatures = sigList };
            }
            return new PollResult();
        }

        private static ProviderResult ErrorResult(string maGiaoDich, string error)
        {
            return new ProviderResult
            {
                MaGiaoDich = maGiaoDich, IsWaiting = false, ErrorMessage = error,
                ErrorCode = Enums.ErrorCode.UnexpectedException,
                MaTrangThaiKy = SignHelper.MA_TRANG_THAI_KY_KHONG_THANH_CONG
            };
        }

        private class CertInfoResult { public byte[]? Certificate { get; set; } public string? SignatureAlgorithm { get; set; } }
        private class PollResult { public bool IsWaiting { get; set; } public List<string>? Signatures { get; set; } }
    }
}
