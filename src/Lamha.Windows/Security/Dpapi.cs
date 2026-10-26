using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Security.Cryptography;
using Lamha.Core.Abstractions;

namespace Lamha.Windows.Security;

/// <summary>
/// Windows' data protection (DPAPI) for this user: what's encrypted can be decrypted only by the same Windows account,
/// on this PC or (with a roaming profile) another. Clipboard history's contents. Lamha 2 used the same through
/// Electron's safeStorage.
/// </summary>
public sealed unsafe class Dpapi : IDataProtector
{
    /// <summary>Mixed into every encryption, so another program using DPAPI for this user can't decrypt Lamha's data by accident.</summary>
    private static readonly byte[] Entropy = "Lamha 3 clipboard history"u8.ToArray();

    public byte[] Protect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* p = data, e = Entropy)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)data.Length, pbData = p };
            var entropy = new CRYPT_INTEGER_BLOB { cbData = (uint)Entropy.Length, pbData = e };
            CRYPT_INTEGER_BLOB output;
            if (!PInvoke.CryptProtectData(&input, null, &entropy, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
            {
                throw new InvalidOperationException("CryptProtectData failed");
            }

            return Take(output);
        }
    }

    public byte[]? Unprotect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* p = data, e = Entropy)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)data.Length, pbData = p };
            var entropy = new CRYPT_INTEGER_BLOB { cbData = (uint)Entropy.Length, pbData = e };
            CRYPT_INTEGER_BLOB output;
            return PInvoke.CryptUnprotectData(&input, null, &entropy, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output)
                ? Take(output)
                : null;
        }
    }

    /// <summary>
    /// Data this Windows user's programs encrypted without entropy, or null: Electron's own key in Lamha 2's
    /// <c>Local State</c>, which its safeStorage encrypted the API keys and clipboard history with.
    /// </summary>
    public static byte[]? UnprotectForUser(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* p = data)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)data.Length, pbData = p };
            CRYPT_INTEGER_BLOB output;
            return PInvoke.CryptUnprotectData(&input, null, null, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output)
                ? Take(output)
                : null;
        }
    }

    /// <summary>Copies the output and frees Windows' buffer (cleared first: it held the plain text, or a key).</summary>
    private static byte[] Take(CRYPT_INTEGER_BLOB blob)
    {
        var span = new Span<byte>(blob.pbData, (int)blob.cbData);
        var copy = span.ToArray();
        span.Clear();
        PInvoke.LocalFree((HLOCAL)(nint)blob.pbData);
        return copy;
    }
}
