using System.Security.Cryptography;
using System.Text;
using LlmUsageMonitor.Core.Abstractions;

namespace LlmUsageMonitor.Infrastructure.Security;

/// <summary>
/// AES-256-GCM secret protector. The 256-bit data key is generated once and
/// wrapped at rest with Windows DPAPI (per-user); an optional master password
/// can instead derive the key with PBKDF2 so the config is portable.
/// Tokens are self-describing: "enc:v1:" + base64(nonce | tag | ciphertext).
/// </summary>
public sealed class AesSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LlmUsageMonitor.v1");

    private readonly byte[] _key;

    public AesSecretProtector(string keyFilePath, string? masterPassword = null)
    {
        _key = masterPassword is { Length: > 0 }
            ? DeriveFromPassword(masterPassword)
            : LoadOrCreateDpapiKey(keyFilePath);
    }

    public bool IsProtected(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (IsProtected(plaintext)) return plaintext;

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        if (!IsProtected(token))
        {
            // Tolerate legacy plaintext values.
            return token;
        }

        try
        {
            var payload = Convert.FromBase64String(token[Prefix.Length..]);
            if (payload.Length < NonceSize + TagSize) return string.Empty;

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var cipher = payload.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] LoadOrCreateDpapiKey(string keyFilePath)
    {
        var dir = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(keyFilePath))
        {
            try
            {
                var wrapped = File.ReadAllBytes(keyFilePath);
                var raw = ProtectedData.Unprotect(wrapped, Entropy, DataProtectionScope.CurrentUser);
                if (raw.Length == KeySize) return raw;
            }
            catch
            {
                // fall through and regenerate
            }
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            var wrapped = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyFilePath, wrapped);
        }
        catch
        {
            // DPAPI unavailable: keep key in memory for this session only.
        }

        return key;
    }

    private static byte[] DeriveFromPassword(string password)
    {
        // Fixed salt keeps derivation deterministic so the same password opens
        // the same config on another machine; security rests on password entropy.
        var salt = Encoding.UTF8.GetBytes("LlmUsageMonitor.pbkdf2.v1");
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 300_000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
