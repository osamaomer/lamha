using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Lamha.Data.Import;

/// <summary>
/// Undoes Electron's safeStorage on Windows, which Lamha 2 encrypted its API keys and clipboard history with
/// (Chromium's OSCrypt): an AES-256 key kept in <c>Local State</c> as "DPAPI" + the key encrypted by Windows for this
/// user, and each value as "v10" + a 12-byte nonce + AES-GCM's ciphertext and 16-byte tag.
/// </summary>
internal sealed class ElectronSafeStorage
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] KeyPrefix = "DPAPI"u8.ToArray();
    private static readonly byte[] ValuePrefix = "v10"u8.ToArray();

    private readonly byte[] _key;

    private ElectronSafeStorage(byte[] key) => _key = key;

    /// <summary>
    /// The key from <c>Local State</c>, or null when it isn't there or Windows can't decrypt it (the folder came from
    /// another Windows account).
    /// </summary>
    /// <param name="localState">Lamha 2's <c>Local State</c> file.</param>
    /// <param name="unprotect">Windows' DPAPI for this user, without entropy (Lamha.Windows' <c>Dpapi.UnprotectForUser</c>).</param>
    public static ElectronSafeStorage? Open(string localState, Func<byte[], byte[]?> unprotect)
    {
        try
        {
            var encoded = (JsonNode.Parse(File.ReadAllText(localState))?["os_crypt"]?["encrypted_key"] as JsonValue)?.GetValue<string>();
            if (encoded is null)
            {
                return null;
            }

            var wrapped = Convert.FromBase64String(encoded);
            if (!wrapped.AsSpan().StartsWith(KeyPrefix))
            {
                return null;
            }

            return unprotect(wrapped[KeyPrefix.Length..]) is { Length: 32 } key ? new ElectronSafeStorage(key) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The text <paramref name="data"/> was made from, or null when it can't be decrypted or was changed.</summary>
    public string? Decrypt(byte[] data)
    {
        if (!data.AsSpan().StartsWith(ValuePrefix) || data.Length < ValuePrefix.Length + NonceSize + TagSize)
        {
            return null;
        }

        var nonce = data.AsSpan(ValuePrefix.Length, NonceSize);
        var cipher = data.AsSpan(ValuePrefix.Length + NonceSize, data.Length - ValuePrefix.Length - NonceSize - TagSize);
        var tag = data.AsSpan(data.Length - TagSize);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            plain.AsSpan().Clear();
        }
    }

    /// <summary>For tests: a value as Electron writes it, with <paramref name="key"/>.</summary>
    internal static byte[] Encrypt(byte[] key, string text)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(text);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        return [.. ValuePrefix, .. nonce, .. cipher, .. tag];
    }
}
