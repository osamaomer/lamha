using System.Globalization;
using System.Text.RegularExpressions;

namespace Lamha.Core.Money;

/// <summary>An amount of money in a currency (an ISO 4217 code: "USD", "SAR").</summary>
public readonly record struct Price(decimal Amount, string Currency);

/// <summary>
/// Finds a price in the selected text, for "prices in your currency" (new in Lamha 3, phase 1's card design): "$49.99",
/// "€20", "1,200 SAR", "SAR 1,200", "٥٠ ريال", "20 dollars". The whole selection must be the price: a price inside a
/// sentence is a sentence to translate.
/// </summary>
public static partial class Prices
{
    /// <summary>Longer than this, the selection isn't just a price.</summary>
    private const int MaxLength = 40;

    /// <summary>
    /// The ISO codes a selection may name ("1,200 SAR"), and the currencies Settings offers; the app has their names in
    /// both languages.
    /// </summary>
    public static IReadOnlyList<string> Named { get; } =
    [
        "USD", "EUR", "GBP", "SAR", "AED", "EGP", "KWD", "QAR", "BHD", "OMR", "JOD", "MAD", "DZD", "TND", "LYD", "IQD", "LBP",
        "SYP", "YER", "SDG", "TRY", "JPY", "CNY", "INR", "PKR", "CAD", "AUD", "CHF", "KRW", "RUB", "MYR", "IDR",
    ];

    private static readonly HashSet<string> Codes = [.. Named];

    /// <summary>Signs and names, longest first where one starts another ("US$" before "$", "ريال سعودي" before "ريال").</summary>
    private static readonly (string Mark, string Currency)[] Marks =
    [
        ("US$", "USD"), ("CA$", "CAD"), ("C$", "CAD"), ("AU$", "AUD"), ("A$", "AUD"), ("$", "USD"), ("€", "EUR"), ("£", "GBP"),
        ("¥", "JPY"), ("₹", "INR"), ("₺", "TRY"), ("₩", "KRW"), ("₽", "RUB"),
        ("ر.س", "SAR"), ("د.إ", "AED"), ("ج.م", "EGP"), ("د.ك", "KWD"), ("ر.ق", "QAR"), ("د.ب", "BHD"), ("ر.ع", "OMR"),
        ("د.أ", "JOD"), ("د.م", "MAD"),
        ("ريال سعودي", "SAR"), ("ريال قطري", "QAR"), ("ريال عماني", "OMR"), ("ريال عُماني", "OMR"), ("ريال يمني", "YER"),
        ("درهم إماراتي", "AED"), ("درهم مغربي", "MAD"), ("دينار كويتي", "KWD"), ("دينار بحريني", "BHD"), ("دينار أردني", "JOD"),
        ("دينار عراقي", "IQD"), ("دينار جزائري", "DZD"), ("دينار تونسي", "TND"), ("دينار ليبي", "LYD"), ("جنيه مصري", "EGP"),
        ("جنيه إسترليني", "GBP"), ("جنيه سوداني", "SDG"), ("دولار أمريكي", "USD"), ("دولار كندي", "CAD"), ("دولار أسترالي", "AUD"),
        ("ليرة تركية", "TRY"), ("ليرة لبنانية", "LBP"), ("ليرة سورية", "SYP"), ("فرنك سويسري", "CHF"), ("ين ياباني", "JPY"),
        ("ريالات", "riyal"), ("ريال", "riyal"), ("دراهم", "dirham"), ("درهم", "dirham"), ("دنانير", "dinar"), ("دينار", "dinar"),
        ("جنيهات", "EGP"), ("جنيه", "EGP"), ("دولارات", "USD"), ("دولار", "USD"), ("يورو", "EUR"), ("ليرات", "lira"), ("ليرة", "lira"),
        ("dollars", "USD"), ("dollar", "USD"), ("euros", "EUR"), ("euro", "EUR"), ("pounds", "GBP"), ("pound", "GBP"),
        ("riyals", "riyal"), ("riyal", "riyal"), ("rials", "riyal"), ("rial", "riyal"), ("dirhams", "dirham"), ("dirham", "dirham"),
        ("dinars", "dinar"), ("dinar", "dinar"), ("yen", "JPY"),
    ];

    /// <summary>A name several countries share: the user's own when it's one of them, else the most likely.</summary>
    private static readonly Dictionary<string, (string[] Mine, string Otherwise)> Shared = new()
    {
        ["riyal"] = (["SAR", "QAR", "OMR", "YER"], "SAR"),
        ["dirham"] = (["AED", "MAD"], "AED"),
        ["dinar"] = (["KWD", "BHD", "JOD", "IQD", "DZD", "TND", "LYD"], "KWD"),
        ["lira"] = (["TRY", "LBP", "SYP"], "TRY"),
    };

    /// <summary>
    /// The price <paramref name="text"/> is, or null. <paramref name="mine"/> is the user's currency: a riyal, dirham,
    /// dinar or lira without its country is the user's own when theirs is one.
    /// </summary>
    public static Price? Find(string? text, string mine)
    {
        var t = Digits((text ?? "").Trim());
        if (t.Length is 0 or > MaxLength)
        {
            return null;
        }

        var m = Shape().Match(t);
        if (!m.Success)
        {
            return null;
        }

        var before = m.Groups["before"].Value.Trim();
        var after = m.Groups["after"].Value.Trim();
        if ((before.Length > 0) == (after.Length > 0))
        {
            return null; // a sign or a name on one side, not both or neither
        }

        var currency = CurrencyOf(before.Length > 0 ? before : after, mine);
        return currency is not null && Amount(m.Groups["number"].Value) is { } amount && amount > 0 ? new Price(amount, currency) : null;
    }

    private static string? CurrencyOf(string mark, string mine)
    {
        if (mark.Length == 3 && Codes.Contains(mark.ToUpperInvariant()) && mark.All(char.IsAsciiLetter))
        {
            return mark.ToUpperInvariant();
        }

        foreach (var (sign, currency) in Marks)
        {
            if (string.Equals(mark, sign, StringComparison.OrdinalIgnoreCase))
            {
                return Shared.TryGetValue(currency, out var shared) ? (shared.Mine.Contains(mine) ? mine : shared.Otherwise) : currency;
            }
        }

        return null;
    }

    /// <summary>"1,200.50", "1.200,50", "1 200", "20,5": the last separator followed by one or two digits is the decimal point.</summary>
    private static decimal? Amount(string number)
    {
        var n = number.Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        var last = n.LastIndexOfAny([',', '.']);
        if (last >= 0 && n.Length - last - 1 is 1 or 2)
        {
            n = n[..last].Replace(",", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal) + "." + n[(last + 1)..];
        }
        else
        {
            n = n.Replace(",", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);
        }

        return decimal.TryParse(n, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Arabic-Indic and Persian digits, and Arabic's decimal and thousands signs, as ASCII.</summary>
    private static string Digits(string text) => string.Create(text.Length, text, (span, s) =>
    {
        for (var i = 0; i < s.Length; i++)
        {
            span[i] = s[i] switch
            {
                >= '٠' and <= '٩' => (char)('0' + (s[i] - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (s[i] - '۰')),
                '٫' => '.',
                '٬' => ',',
                _ => s[i],
            };
        }
    });

    /// <summary>A sign or name, the number, a sign or name: one of the two is empty.</summary>
    [GeneratedRegex(@"^(?<before>[^\d\s]{0,15}(?:\s[^\d\s]{1,12})?)\s*(?<number>\d{1,3}(?:[,.  ]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s*(?<after>[^\d\s]{0,15}(?:\s[^\d\s]{1,12})?)$")]
    private static partial Regex Shape();
}
