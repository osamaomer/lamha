using System.Text;
using global::Windows.Win32;
using global::Windows.Win32.Security.Credentials;
using Lamha.Core.Abstractions;

namespace Lamha.Windows.Security;

/// <summary>
/// API keys in Windows' Credential Manager (the vault behind Credential Locker), never in Lamha's database or log
/// (decision 0006). Each key is a generic credential named "Lamha/claude", "Lamha/gemini"…, readable only by this
/// Windows user; Control Panel → Credential Manager → Windows Credentials lists them.
/// </summary>
/// <param name="prefix">Names start with it: "Lamha/" in the app, something else in tests.</param>
public sealed unsafe class CredentialStore(string prefix = "Lamha/") : ISecretStore
{
    /// <summary>Credential Manager's limit for a generic credential's secret (CRED_MAX_CREDENTIAL_BLOB_SIZE).</summary>
    private const int MaxBytes = 5 * 512;

    public string? Get(string name)
    {
        if (!PInvoke.CredRead(prefix + name, CRED_TYPE.CRED_TYPE_GENERIC, out var credential))
        {
            return null;
        }

        try
        {
            return credential->CredentialBlobSize == 0
                ? null
                : Encoding.UTF8.GetString(credential->CredentialBlob, (int)credential->CredentialBlobSize);
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    public void Set(string name, string? value)
    {
        var target = prefix + name;
        if (string.IsNullOrEmpty(value))
        {
            PInvoke.CredDelete(target, CRED_TYPE.CRED_TYPE_GENERIC);
            return;
        }

        var secret = Encoding.UTF8.GetBytes(value);
        if (secret.Length > MaxBytes)
        {
            throw new ArgumentException("A key this long can't be kept by Windows.", nameof(value));
        }

        try
        {
            fixed (char* t = target, user = "Lamha")
            fixed (byte* blob = secret)
            {
                var credential = new CREDENTIALW
                {
                    Type = CRED_TYPE.CRED_TYPE_GENERIC,
                    TargetName = t,
                    UserName = user,
                    CredentialBlob = blob,
                    CredentialBlobSize = (uint)secret.Length,
                    // This PC only: a key isn't sent to other PCs with a roaming profile.
                    Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
                };
                if (!PInvoke.CredWrite(&credential, 0))
                {
                    throw new InvalidOperationException("CredWrite failed");
                }
            }
        }
        finally
        {
            Array.Clear(secret);
        }
    }
}
