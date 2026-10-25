namespace Lamha.Core.Abstractions;

/// <summary>
/// API keys, kept by Windows (Credential Locker) and never in the database or the log. Names are the providers':
/// "claude", "gemini", "openai", "custom".
/// </summary>
public interface ISecretStore
{
    /// <summary>The key saved under <paramref name="name"/>, or null.</summary>
    string? Get(string name);

    /// <summary>Saves <paramref name="value"/>; null or "" removes it.</summary>
    void Set(string name, string? value);
}
