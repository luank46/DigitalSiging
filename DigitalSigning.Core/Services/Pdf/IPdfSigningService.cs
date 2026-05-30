using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Services.Pdf
{
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
        /// Returns the hash to be sent to the CA provider.
        /// </summary>
        Task<byte[]> ComputeHashAsync(byte[] pdfBytes, CancellationToken ct = default);

        /// <summary>
        /// Embed a signature into a PDF document.
        /// Creates a signature field, applies the signature value, and returns the signed PDF bytes.
        /// NOTE: Full implementation requires a PDF library with digital signature support.
        /// </summary>
        Task<byte[]> EmbedSignatureAsync(byte[] pdfBytes, byte[] signatureValue,
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
