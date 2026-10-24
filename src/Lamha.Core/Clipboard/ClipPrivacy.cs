using System.Text.RegularExpressions;

namespace Lamha.Core.Clipboard;

/// <summary>
/// Clipboard history's privacy rules: the programs whose copies are never recorded, and bank-card numbers. Lamha 2's
/// desktop/clipboard-privacy.js.
/// </summary>
public static partial class ClipPrivacy
{
    /// <summary>Password managers, excluded out of the box (they usually mark their copies too, but not always).</summary>
    public static IReadOnlyList<string> DefaultExcludedApps { get; } =
        ["keepass.exe", "keepassxc.exe", "1password.exe", "bitwarden.exe", "enpass.exe", "dashlane.exe", "nordpass.exe"];

    /// <summary>
    /// "KeePassXC", <c>"C:\…\KeePassXC.exe "</c> → "keepassxc.exe"; "" when it can't be a program's name. Lamha 2's
    /// <c>exeName()</c>.
    /// </summary>
    public static string ExeName(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.StartsWith('"'))
        {
            s = s[1..];
        }

        if (s.EndsWith('"'))
        {
            s = s[..^1];
        }

        s = s[(Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/')) + 1)..].ToLowerInvariant();
        if (s.Length > 0 && !s.EndsWith(".exe", StringComparison.Ordinal))
        {
            s += ".exe";
        }

        return s != ".exe" && ExeNamePattern().IsMatch(s) ? s : "";
    }

    /// <summary>
    /// A bank-card number on its own: 13–19 digits, single spaces or dashes allowed between them, passing the Luhn
    /// check. Phone numbers, IBANs, amounts and text around the number aren't. Lamha 2's <c>isCardNumber()</c>.
    /// </summary>
    public static bool IsCardNumber(string? text)
    {
        var t = (text ?? "").Trim();
        if (!CardNumberPattern().IsMatch(t))
        {
            return false;
        }

        var sum = 0;
        var position = 0;
        for (var i = t.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(t[i]))
            {
                continue;
            }

            var d = t[i] - '0';
            if (position++ % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
        }

        return sum % 10 == 0;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} _.()+&'-]{1,80}\.exe$")]
    private static partial Regex ExeNamePattern();

    [GeneratedRegex(@"^[0-9](?:[ -]?[0-9]){12,18}$")]
    private static partial Regex CardNumberPattern();
}
