using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core.Money;

namespace Lamha.App.ViewModels.Card;

/// <summary>
/// A price in the user's currency, as phase 1's card design shows it: the price as selected, "In your currency", the
/// amount, and "1 US dollar = 3.75 riyals · rate of 25 October". The card shows it, and so does the Translate page.
/// </summary>
public sealed class PriceAnswer
{
    private PriceAnswer(string selected, Conversion conversion, IAnswerActions actions)
    {
        Selected = selected.Trim();
        Value = $"{Localization.Money(conversion.Amount)} {Name(conversion.To, "Many")}";
        RateLine = Localization.Format(
            "Card_RateLine",
            $"{Localization.Number(1)} {Name(conversion.From.Currency, "One")}",
            $"{Localization.Rate(conversion.Rate)} {Name(conversion.To, "Short")}",
            Localization.DayAndMonth(conversion.RateDay));
        Copy = new RelayCommand(() => actions.Copy(Value));
        // ExchangeRate-API's terms ask for its credit, as a link, where its rates are shown.
        Links = [new LinkItem(Glyph.External, CurrencyService.Credit, "", new RelayCommand(() => actions.OpenLink(new Uri(CurrencyService.CreditUrl))))];
    }

    /// <summary>
    /// "Prices in your currency" (phase 1's design): <paramref name="text"/> in the user's currency when it's a price
    /// alone in another currency, or null to look it up as usual (not a price, already the user's, an unknown currency,
    /// no rates).
    /// </summary>
    public static async Task<PriceAnswer?> ForAsync(Brain brain, string text, IAnswerActions actions, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var mine = brain.Money.Mine(brain.Settings.Current);
        if (Prices.Find(text, mine) is not { } price || price.Currency == mine)
        {
            return null;
        }

        var conversion = await brain.Money.ConvertAsync(price, mine, cancel).ConfigureAwait(true);
        return conversion is null || cancel.IsCancellationRequested ? null : new PriceAnswer(text, conversion, actions);
    }

    public string Selected { get; }

    public string Label { get; } = Localization.Get("Card_InYourCurrency");

    /// <summary>"187.46 Saudi riyals", "١٨٧٫٤٦ ريال سعودي".</summary>
    public string Value { get; }

    public string RateLine { get; }

    /// <summary>Copies the amount in the user's currency.</summary>
    public IRelayCommand Copy { get; }

    /// <summary>The rates' credit, at the foot.</summary>
    public List<LinkItem> Links { get; }

    /// <summary>What Narrator says when it shows.</summary>
    public string Spoken => $"{Selected}: {Value}";

    /// <summary>A currency's name ("Currency_SAR_Many"), or its code for a currency Lamha has no name for.</summary>
    private static string Name(string code, string form)
    {
        var key = $"Currency_{code}_{form}";
        var name = Localization.Get(key);
        return name == key ? code : name;
    }
}
