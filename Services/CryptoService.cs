using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BrowserReporterService.Services
{
    public class CryptoService
    {
        private const string KeyString = "BrowserReporter2024!MasterKey";

        private static byte[] GetKey()
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(KeyString));
        }

        public string EncryptConfig(string plaintextJson)
        {
            var key = GetKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintextJson);
            var ciphertextBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var checksum = BitConverter.ToString(SHA256.HashData(plaintextBytes)).Replace("-", "").ToLowerInvariant();

            var envelope = new SecureConfigEnvelope
            {
                Version = "1.0",
                EncryptedData = Convert.ToBase64String(ciphertextBytes),
                Iv = Convert.ToBase64String(iv),
                Checksum = checksum
            };

            return JsonConvert.SerializeObject(envelope, Formatting.Indented);
        }

        public AppConfig? DecryptConfig(string encryptedEnvelopeJson)
        {
            var envelope = JsonConvert.DeserializeObject<SecureConfigEnvelope>(encryptedEnvelopeJson);
            if (envelope == null) return null;

            var key = GetKey();

            // Verify HMAC signature if present
            if (!string.IsNullOrEmpty(envelope.Signature))
            {
                var encryptedBytes = Convert.FromBase64String(envelope.EncryptedData);
                var ivBytes = Convert.FromBase64String(envelope.Iv);
                
                using var hmac = new HMACSHA256(key);
                hmac.TransformBlock(encryptedBytes, 0, encryptedBytes.Length, null, 0);
                hmac.TransformBlock(ivBytes, 0, ivBytes.Length, null, 0);
                hmac.TransformFinalBlock(Encoding.UTF8.GetBytes(envelope.Version), 0, Encoding.UTF8.GetBytes(envelope.Version).Length);
                
                var calculatedSignature = BitConverter.ToString(hmac.Hash!).Replace("-", "").ToLowerInvariant();
                var providedSignature = envelope.Signature.ToLowerInvariant();
                
                if (calculatedSignature != providedSignature)
                {
                    throw new CryptographicException("HMAC signature verification failed. The configuration may have been tampered with.");
                }
            }

            var iv = Convert.FromBase64String(envelope.Iv);
            var ciphertext = Convert.FromBase64String(envelope.EncryptedData);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            var plaintextJson = Encoding.UTF8.GetString(plaintextBytes);
            
            var calculatedChecksum = BitConverter.ToString(SHA256.HashData(plaintextBytes)).Replace("-", "").ToLowerInvariant();
            if (calculatedChecksum != envelope.Checksum)
            {
                throw new CryptographicException("Checksum validation failed. The decrypted data may have been tampered with.");
            }

            return JsonConvert.DeserializeObject<AppConfig>(plaintextJson);
        }
    }
} 