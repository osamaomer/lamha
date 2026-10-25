using Lamha.App.Services;
using Lamha.Core.Money;

namespace Lamha.App.ViewModels.Card;

/// <summary>
/// A price in the user's currency, as phase 1's card design shows it: the price as selected, "In your currency", the
/// amount, and "1 US dollar = 3.75 riyals · rate of 25 October".
/// </summary>
public sealed class PriceAnswer(string selected, Conversion conversion)
{
    public string Selected { get; } = selected.Trim();

    public string Label { get; } = Localization.Get("Card_InYourCurrency");

    /// <summary>"187.46 Saudi riyals", "١٨٧٫٤٦ ريال سعودي".</summary>
    public string Value { get; } = $"{Localization.Money(conversion.Amount)} {Name(conversion.To, "Many")}";

    public string RateLine { get; } = Localization.Format(
        "Card_RateLine",
        $"{Localization.Number(1)} {Name(conversion.From.Currency, "One")}",
        $"{Localization.Rate(conversion.Rate)} {Name(conversion.To, "Short")}",
        Localization.DayAndMonth(conversion.RateDay));

    /// <summary>What Copy copies: the amount in the user's currency.</summary>
    public string CopyText => Value;

    /// <summary>A currency's name ("Currency_SAR_Many"), or its code for a currency Lamha has no name for.</summary>
    private static string Name(string code, string form)
    {
        var key = $"Currency_{code}_{form}";
        var name = Localization.Get(key);
        return name == key ? code : name;
    }
}
