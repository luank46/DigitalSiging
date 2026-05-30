using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DigitalSigning.Core.Helpers
{
    /// <summary>
    /// Utility for GZip compression/decompression of message payloads.
    /// Used to reduce message size when publishing large payloads to RabbitMQ.
    /// </summary>
    public static class GZipHelper
    {
        /// <summary>
        /// Compresses a string using GZip and returns Base64-encoded result.
        /// </summary>
        public static string Compress(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var bytes = Encoding.UTF8.GetBytes(text);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(output.ToArray());
        }

        /// <summary>
        /// Decompresses a Base64-encoded GZip string back to original text.
        /// </summary>
        public static string? Decompress(string compressed)
        {
            if (string.IsNullOrEmpty(compressed))
                return compressed;

            try
            {
                var bytes = Convert.FromBase64String(compressed);
                using var input = new MemoryStream(bytes);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                // Not compressed or invalid — return as-is
                return compressed;
            }
        }

        /// <summary>
        /// Compresses a byte array using GZip.
        /// </summary>
        public static byte[] CompressBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;

            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                gzip.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        /// <summary>
        /// Decompresses a GZip-compressed byte array.
        /// </summary>
        public static byte[] DecompressBytes(byte[] compressed)
        {
            if (compressed == null || compressed.Length == 0)
                return compressed;

            try
            {
                using var input = new MemoryStream(compressed);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return compressed;
            }
        }

        /// <summary>
        /// Returns true if the byte array appears to be GZip compressed (magic bytes 0x1F, 0x8B).
        /// </summary>
        public static bool IsCompressed(byte[] data)
        {
            return data is { Length: >= 2 } && data[0] == 0x1F && data[1] == 0x8B;
        }

        /// <summary>
        /// Returns true if the Base64 string decodes to GZip compressed data.
        /// </summary>
        public static bool IsCompressedBase64(string base64)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                return IsCompressed(bytes);
            }
            catch
            {
                return false;
            }
        }
    }
}
