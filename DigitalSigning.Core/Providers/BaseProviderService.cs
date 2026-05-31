using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Services.Pdf;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Abstract base provider chứa shared logic xử lý ký số.
    /// Mapping từ ASignService&lt;T&gt; legacy, refactor cho kiến trúc microservice.
    /// </summary>
    public abstract class BaseProviderService
    {
        protected readonly IGridFsService GridFs;
        protected readonly ITransactionService TxService;
        protected readonly ILogger Logger;
        protected readonly IPdfSigningService PdfSigning;

        protected BaseProviderService(
            IGridFsService gridFs,
            ITransactionService txService,
            ILogger logger,
            IPdfSigningService? pdfSigning = null)
        {
            GridFs = gridFs;
            TxService = txService;
            Logger = logger;
            PdfSigning = pdfSigning ?? new PdfSigningService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfSigningService>.Instance);
        }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Tạo hash data cho tất cả file theo loại (XML, PDF, hoặc HASH).
        /// PDF hash uses PdfPig for byte-range computation.
        /// </summary>
        public virtual async Task CreateHashData(ProviderSignRequest request, CancellationToken ct = default)
        {
            if (request.FileType == SignHelper.FILE_TYPE_XML)
                await ComputeDsig(request, ct);
            else if (request.FileType == SignHelper.FILE_TYPE_PDF)
                await ComputePdfHash(request, ct);
            else if (request.FileType == SignHelper.FILE_TYPE_HASH)
                CreateHash(request);
            else
                Logger.LogWarning("CreateHashData: unsupported file type {FileType} for {MaGiaoDich}",
                    request.FileType, request.MaGiaoDich);
        }

        /// <summary>
        /// Compute PDF hash using PdfPig byte-range computation (PAdES standard).
        /// Uses IPdfSigningService for proper byte-range hash.
        /// </summary>
        public virtual async Task ComputePdfHash(ProviderSignRequest request, CancellationToken ct = default)
        {
            if (request.Files == null) return;

            foreach (var (file, idx) in request.Files.Select((f, i) => (f, i)))
            {
                byte[] fileBytes;
                if (!string.IsNullOrEmpty(file.FileByteId))
                    fileBytes = await GridFs.DownloadFileAsync(file.FileByteId, ct);
                else
                    continue;

                if (fileBytes.Length == 0) continue;

                // Use IPdfSigningService to compute PAdES byte-range hash
                var (hash, pdfContext) = await PdfSigning.ComputeHashAsync(fileBytes, ct);
                request.DataHashes ??= new List<string>();
                request.DataHashes.Add(Convert.ToBase64String(hash));

                // Store the context for later EmbedSignatureAsync call
                request.PdfContexts ??= new Dictionary<string, object>();
                request.PdfContexts[file.FileByteId ?? idx.ToString()] = pdfContext;

                Logger.LogDebug("ComputePdfHash: PAdES hash computed for file {FileName} ({Length} bytes)",
                    file.FileName, fileBytes.Length);
            }
        }

        /// <summary>
        /// Nhúng chữ ký vào tài liệu (XML, PDF, hoặc HASH).
        /// </summary>
        public virtual async Task<AppendSignatureResult> AppendSignature(ProviderSignRequest request, CancellationToken ct = default)
        {
            if (request.FileType == SignHelper.FILE_TYPE_XML)
                return await SignXml(request, ct);
            else if (request.FileType == SignHelper.FILE_TYPE_PDF)
                return await SignPdf(request, ct);
            else
                return GetSignatures(request);
        }

        // ── XML: ComputeDsig ────────────────────────────────────────────

        public virtual async Task ComputeDsig(ProviderSignRequest request, CancellationToken ct = default)
        {
            if (request.Certificate == null || request.Certificate.Length == 0)
            {
                Logger.LogError("ComputeDsig: Certificate is null for {MaGiaoDich}", request.MaGiaoDich);
                return;
            }

            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(request.Certificate);
            var isRsa = cert.PublicKey.Oid.FriendlyName == "RSA";
            request.SignatureAlgorithm = isRsa ? "1.2.840.113549.1.1.1" : "1.2.840.10045.4.3.2";
            var signatureMethod = isRsa
                ? SignedXml.XmlDsigRSASHA256Url
                : "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";

            if (request.Files == null) return;

            foreach (var file in request.Files)
            {
                byte[] fileBytes;
                if (!string.IsNullOrEmpty(file.FileByteId))
                    fileBytes = await GridFs.DownloadFileAsync(file.FileByteId, ct);
                else if (!string.IsNullOrEmpty(file.FileUrl))
                    fileBytes = Array.Empty<byte>(); // would need HTTP download
                else
                    continue;

                if (fileBytes.Length == 0) continue;

                using var memoryStream = new MemoryStream(fileBytes);
                var xmlDoc = new XmlDocument { PreserveWhitespace = true };
                xmlDoc.Load(memoryStream);

                var signedXml = new SignedXmlWithId(xmlDoc)
                {
                    SigningKey = isRsa
                        ? (AsymmetricAlgorithm)RSA.Create(2048)
                        : ECDsa.Create(ECCurve.NamedCurves.nistP256)
                };
                signedXml.SignedInfo.SignatureMethod = signatureMethod;

                // KeyInfo
                var keyInfo = new KeyInfo();
                var kiData = new KeyInfoX509Data();
                kiData.AddSubjectName(cert.SubjectName.Name);
                kiData.AddIssuerSerial(cert.Issuer, cert.GetSerialNumberString());
                kiData.AddCertificate(cert);
                keyInfo.AddClause(kiData);
                signedXml.KeyInfo = keyInfo;

                // References
                if (!string.IsNullOrEmpty(file.NodeDataSignId))
                {
                    var reference = new Reference { Uri = $"#{file.NodeDataSignId}" };
                    reference.AddTransform(new XmlDsigEnvelopedSignatureTransform(true));
                    signedXml.AddReference(reference);
                }
                if (file.NodeDataSignIds != null)
                {
                    foreach (var nodeId in file.NodeDataSignIds)
                    {
                        var reference = new Reference { Uri = $"#{nodeId}" };
                        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform(true));
                        signedXml.AddReference(reference);
                    }
                }

                // Signature properties with timestamp
                var idSignature = Guid.NewGuid().ToString();
                signedXml.Signature.Id = $"sigid-{idSignature}";

                var dataObject = new DataObject { Id = $"signobject-{idSignature}" };
                var element1 = xmlDoc.CreateElement("SignatureProperties");
                var element3 = xmlDoc.CreateElement("SignatureProperty");
                element3.SetAttribute("Target", $"#sigid-{idSignature}");

                var signingTime = xmlDoc.CreateElement("SigningTime", "http://example.org/#signatureProperties");
                signingTime.InnerText = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                element3.AppendChild(signingTime);
                element1.AppendChild(element3);
                dataObject.Data = element1.SelectNodes(".");
                signedXml.Signature.AddObject(dataObject);

                var timeRef = new Reference { Uri = $"#signobject-{idSignature}" };
                timeRef.AddTransform(new XmlDsigExcC14NTransform());
                timeRef.DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256";
                signedXml.AddReference(timeRef);

                signedXml.ComputeSignature();

                // Place signature in document
                if (!string.IsNullOrEmpty(file.NodeSignerPath))
                {
                    var elements = xmlDoc.SelectNodes(file.NodeSignerPath);
                    if (elements != null && elements.Count > 0)
                    {
                        var nodeCKy = elements[^1];
                        if (!string.IsNullOrEmpty(file.NodeSignerText))
                        {
                            foreach (XmlNode node in elements)
                            {
                                var signerId = node.SelectSingleNode("SO_CCCD");
                                if (signerId?.InnerText == file.NodeSignerText) { nodeCKy = node; break; }
                            }
                        }
                        if (!string.IsNullOrEmpty(file.NodeSignerId))
                        {
                            foreach (XmlNode node in elements)
                            {
                                if (node.Attributes?["Id"]?.Value == file.NodeSignerId) { nodeCKy = node; break; }
                            }
                        }

                        // Remove existing signatures
                        var existingSigs = nodeCKy.ChildNodes.Cast<XmlNode>()
                            .Where(n => n.LocalName == "Signature").ToList();
                        foreach (var sig in existingSigs) nodeCKy.RemoveChild(sig);

                        // Add NGAY_KY for text mode
                        if (!string.IsNullOrEmpty(file.NodeSignerText))
                        {
                            var ngayKyNode = nodeCKy.SelectSingleNode("NGAY_KY");
                            if (ngayKyNode != null)
                                ngayKyNode.InnerText = DateTime.UtcNow.ToString("dd/MM/yyyy");
                            else
                            {
                                var newNode = xmlDoc.CreateElement("NGAY_KY");
                                newNode.InnerText = DateTime.UtcNow.ToString("dd/MM/yyyy");
                                nodeCKy.AppendChild(newNode);
                            }
                        }
                        else
                        {
                            nodeCKy.AppendChild(signedXml.GetXml());
                        }
                    }
                }

                // Extract hash for provider
                var signedInfo = signedXml.GetXml()?.SelectSingleNode("//SignedInfo");
                if (signedInfo != null)
                {
                    var signedInfoDoc = new XmlDocument();
                    signedInfoDoc.LoadXml(signedInfo.OuterXml);
                    var c14n = new XmlDsigC14NTransform();
                    c14n.LoadInput(signedInfoDoc);
                    var dsig = new StreamReader((Stream)c14n.GetOutput(typeof(Stream))).ReadToEnd();
                    var hashData = SHA256.HashData(Encoding.UTF8.GetBytes(dsig));
                    request.DataHashes ??= new List<string>();
                    request.DataHashes.Add(Convert.ToBase64String(hashData));
                }

                // Save modified document to GridFS
                using var ms = new MemoryStream();
                xmlDoc.Save(ms);
                file.FileByteId = await GridFs.UploadFileAsync(ms.ToArray(),
                    file.FileName ?? "unsigned.xml", ct);
            }
        }

        // ── HASH: CreateHash ───────────────────────────────────────────

        public virtual void CreateHash(ProviderSignRequest request)
        {
            if (request.Files == null) return;
            foreach (var file in request.Files)
            {
                if (!string.IsNullOrEmpty(file.HashData))
                {
                    request.DataHashes ??= new List<string>();
                    request.DataHashes.Add(file.HashData);
                }
            }
        }

        // ── XML: SignXml ────────────────────────────────────────────────

        public virtual async Task<AppendSignatureResult> SignXml(ProviderSignRequest request, CancellationToken ct = default)
        {
            var result = new AppendSignatureResult();
            if (request.Files == null || request.Signatures == null) return result;

            foreach (var (file, index) in request.Files.Select((f, i) => (f, i)))
            {
                if (string.IsNullOrEmpty(file.FileByteId)) continue;

                var fileBytes = await GridFs.DownloadFileAsync(file.FileByteId, ct);
                using var memoryStream = new MemoryStream(fileBytes);
                var xmlDoc = new XmlDocument { PreserveWhitespace = true };
                xmlDoc.Load(memoryStream);

                var signatureTags = xmlDoc.GetElementsByTagName("Signature");
                XmlNode? signatureNode = signatureTags.Count > 0 ? signatureTags[^1] : null;

                if (!string.IsNullOrEmpty(file.NodeSignerPath))
                {
                    var elements = xmlDoc.SelectNodes(file.NodeSignerPath);
                    if (elements != null && elements.Count > 0)
                    {
                        var nodeCKy = elements[^1];
                        if (!string.IsNullOrEmpty(file.NodeSignerText))
                        {
                            foreach (XmlNode node in elements)
                            {
                                var signerId = node.SelectSingleNode("SO_CCCD");
                                if (signerId?.InnerText == file.NodeSignerText) { nodeCKy = node; break; }
                            }
                        }
                        if (!string.IsNullOrEmpty(file.NodeSignerId))
                        {
                            foreach (XmlNode node in elements)
                            {
                                if (node.Attributes?["Id"]?.Value == file.NodeSignerId) { nodeCKy = node; break; }
                            }
                        }
                        foreach (XmlNode child in nodeCKy.ChildNodes)
                        {
                            if (child.Name == "Signature") { signatureNode = child; break; }
                        }
                    }
                }

                if (signatureNode != null && index < request.Signatures.Count)
                {
                    var sigValue = Regex.Replace(request.Signatures[index], @"\r?\n|\r", "");
                    foreach (XmlNode child in signatureNode.ChildNodes)
                    {
                        if (child.Name == "SignatureValue")
                        {
                            child.InnerText = sigValue;
                            break;
                        }
                    }
                }

                using var ms = new MemoryStream();
                xmlDoc.Save(ms);
                var signedBytes = ms.ToArray();

                result.SignedFiles.Add(new SignedFile
                {
                    Md5Hash = file.Md5Hash ?? "",
                    FileName = file.FileName ?? "signed.xml",
                    FileByteId = await GridFs.UploadFileAsync(signedBytes, file.FileName ?? "signed.xml", ct),
                    Signature = index < request.Signatures.Count ? request.Signatures[index] : null,
                    XmlSignature = file.XmlSignature,
                    NodeSignerPath = file.NodeSignerPath
                });
            }

            result.Success = true;
            return result;
        }

        // ── PDF: SignPdf ─────────────────────────────────────────────────

        /// <summary>
        /// Embed signature bytes into PDF files using PAdES byte-range signing.
        /// Uses IPdfSigningService.EmbedSignatureAsync for proper CMS/PKCS#7 embedding.
        /// </summary>
        public virtual async Task<AppendSignatureResult> SignPdf(ProviderSignRequest request, CancellationToken ct = default)
        {
            var result = new AppendSignatureResult();
            if (request.Files == null || request.Signatures == null) return result;

            foreach (var (file, index) in request.Files.Select((f, i) => (f, i)))
            {
                if (string.IsNullOrEmpty(file.FileByteId)) continue;

                var fileBytes = await GridFs.DownloadFileAsync(file.FileByteId, ct);

                if (index < request.Signatures.Count && !string.IsNullOrEmpty(request.Signatures[index]))
                {
                    var sigBytes = Convert.FromBase64String(request.Signatures[index]);

                    // Use IPdfSigningService for proper PAdES signature embedding
                    // ComputeHashAsync must have been called first (during CreateHashData)
                    // to prepare the byte ranges and store the PdfSignatureContext.
                    var pdfContext = request.PdfContexts?.GetValueOrDefault(
                        file.FileByteId ?? index.ToString()) as PdfSignatureContext
                        ?? new PdfSignatureContext(fileBytes, new[] { 0, fileBytes.Length, 0, 0 }); // fallback

                    var signedPdf = await PdfSigning.EmbedSignatureAsync(fileBytes, sigBytes,
                        pdfContext, ct: ct);

                    result.SignedFiles.Add(new SignedFile
                    {
                        Md5Hash = file.Md5Hash ?? "",
                        FileName = file.FileName ?? "signed.pdf",
                        FileByteId = await GridFs.UploadFileAsync(signedPdf, file.FileName ?? "signed.pdf", ct),
                        Signature = request.Signatures[index],
                        XmlSignature = file.XmlSignature,
                        NodeSignerPath = file.NodeSignerPath
                    });
                }
            }

            result.Success = true;
            return result;
        }

        // ── HASH: GetSignatures ─────────────────────────────────────────

        public virtual AppendSignatureResult GetSignatures(ProviderSignRequest request)
        {
            var result = new AppendSignatureResult();
            if (request.Files == null || request.Signatures == null) return result;

            foreach (var (file, index) in request.Files.Select((f, i) => (f, i)))
            {
                result.SignedFiles.Add(new SignedFile
                {
                    Md5Hash = file.Md5Hash ?? "",
                    FileName = file.FileName ?? "signed",
                    Signature = index < request.Signatures.Count ? request.Signatures[index] : null,
                    XmlSignature = file.XmlSignature,
                    NodeSignerPath = file.NodeSignerPath
                });
            }

            result.Success = true;
            return result;
        }

        // ── Internal: SignedXmlWithId ───────────────────────────────────

        internal class SignedXmlWithId : SignedXml
        {
            public SignedXmlWithId(XmlDocument document) : base(document) { }

            public override XmlElement? GetIdElement(XmlDocument document, string idValue)
            {
                var result = base.GetIdElement(document, idValue);
                if (result != null) return result;

                foreach (XmlNode node in document.GetElementsByTagName("*"))
                {
                    if (node is not XmlElement element) continue;
                    foreach (XmlAttribute attr in element.Attributes)
                    {
                        if (string.Equals(attr.Name, "Id", StringComparison.OrdinalIgnoreCase)
                            && attr.Value == idValue)
                            return element;
                    }
                }
                return null;
            }
        }
    }

    /// <summary>
    /// Kết quả của AppendSignature operation.
    /// </summary>
    public class AppendSignatureResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<SignedFile> SignedFiles { get; set; } = new();
    }
}
