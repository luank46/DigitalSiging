using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Services.Pdf
{
    /// <summary>
    /// Opaque token representing the prepared state of a PDF after hash computation.
    /// Returned by <see cref="IPdfSigningService.ComputeHashAsync"/> and consumed by
    /// <see cref="IPdfSigningService.EmbedSignatureAsync"/> to avoid re‑preparing the PDF.
    /// </summary>
    public sealed class PdfSignatureContext
    {
        internal byte[] PreparedPdf { get; }
        internal int[] ByteRange { get; }

        internal PdfSignatureContext(byte[] preparedPdf, int[] byteRange)
        {
            PreparedPdf = preparedPdf;
            ByteRange = byteRange;
        }
    }

    /// <summary>
    /// Service for PDF document operations: reading, hash computation, signature embedding.
    /// Currently uses PdfPig for PDF reading. Full signing support requires a library
    /// capable of PDF digital signatures (e.g. iTextSharp, Aspose.PDF, or similar).
    /// </summary>
    public interface IPdfSigningService
    {
        /// <summary>
        /// Compute the hash of a PDF document for signing.
        /// Implements PDF byte range computation as per PAdES standard.
        /// Returns the hash AND a context token to be passed to <see cref="EmbedSignatureAsync"/>.
        /// </summary>
        Task<(byte[] Hash, PdfSignatureContext Context)> ComputeHashAsync(
            byte[] pdfBytes, CancellationToken ct = default);

        /// <summary>
        /// Embed a signature into a PDF document using the context obtained from <see cref="ComputeHashAsync"/>.
        /// </summary>
        Task<byte[]> EmbedSignatureAsync(
            byte[] pdfBytes, byte[] signatureValue, PdfSignatureContext context,
            string? signatureName = null, CancellationToken ct = default);

        /// <summary>
        /// Get the total number of pages in the PDF.
        /// </summary>
        int GetPageCount(byte[] pdfBytes);

        /// <summary>
        /// Validate whether the file is a valid PDF.
        /// </summary>
        bool IsValidPdf(byte[] pdfBytes);
    }
}
