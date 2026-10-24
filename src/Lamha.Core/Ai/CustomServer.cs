using System.Net;
using System.Net.Sockets;

namespace Lamha.Core.Ai;

/// <summary>A ready-made custom server: the user picks it and adds a key.</summary>
public sealed record CustomServerPreset(string Name, string BaseUrl, bool NeedsKey);

/// <summary>Custom servers that speak OpenAI's Chat Completions (decision 0007).</summary>
public static class CustomServer
{
    public static readonly IReadOnlyList<CustomServerPreset> Presets =
    [
        new("LM Studio", "http://localhost:1234/v1", NeedsKey: false),
        new("OpenRouter", "https://openrouter.ai/api/v1", NeedsKey: true),
        new("Groq", "https://api.groq.com/openai/v1", NeedsKey: true),
    ];

    /// <summary>
    /// The address tidied (no trailing slash), or null when it isn't an http(s) address, or is plain http:// to anywhere
    /// but this PC and the local network: a key must never be sent in the clear.
    /// </summary>
    public static string? Normalize(string? url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
        {
            return null;
        }

        return uri.Scheme == "https" || IsLocal(u) ? u : null;
    }

    /// <summary>The address is on this PC or the local network.</summary>
    public static bool IsLocal(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.IdnHost.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip))
        {
            // A single-label name ("my-pc") is a computer on the local network.
            return !host.Contains('.', StringComparison.Ordinal);
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
    }
}
