using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DigitalSigning.Core.Helpers
{
    /// <summary>
    /// AES-256 encryption helper cho các field nhạy cảm (password, token).
    /// Key được lấy từ environment variable SIGNING_ENCRYPTION_KEY (32 bytes base64).
    /// </summary>
    public static class EncryptionHelper
    {
        private static readonly byte[] Key;
        private static readonly byte[] Iv;

        static EncryptionHelper()
        {
            var keyBase64 = Environment.GetEnvironmentVariable("SIGNING_ENCRYPTION_KEY");
            if (string.IsNullOrEmpty(keyBase64))
            {
                throw new InvalidOperationException(
                    "SIGNING_ENCRYPTION_KEY environment variable is required. " +
                    "Generate a 32-byte base64 key: " +
                    "openssl rand -base64 32");
            }

            var keyBytes = Convert.FromBase64String(keyBase64);
            if (keyBytes.Length == 32)
            {
                Key = keyBytes;
            }
            else
            {
                Key = SHA256.HashData(keyBytes);
            }

            // Fixed IV for deterministic encryption (OK for internal use)
            Iv = new byte[16]; // 16 bytes of zeros
        }

        /// <summary>
        /// Encrypt plaintext → base64-encoded ciphertext.
        /// Returns null if input is null.
        /// </summary>
        public static string? Encrypt(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = Key;
                aes.IV = Iv;

                var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                return Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return plainText; // fallback — return plaintext
            }
        }

        /// <summary>
        /// Decrypt base64-encoded ciphertext → plaintext.
        /// Returns input as-is if not encrypted or decryption fails.
        /// </summary>
        public static string? Decrypt(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = Key;
                aes.IV = Iv;

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                var cipherBytes = Convert.FromBase64String(cipherText);
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return cipherText; // not encrypted or invalid
            }
        }
    }
}
