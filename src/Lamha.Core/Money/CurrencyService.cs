using System.Globalization;
using System.Text.Json.Nodes;
using Lamha.Core.Abstractions;
using Lamha.Core.Net;
using Lamha.Core.Settings;
using Lamha.Core.Translation;

namespace Lamha.Core.Money;

/// <summary>A price in the user's currency, and the rate it was converted at.</summary>
/// <param name="From">The price as selected.</param>
/// <param name="Amount">The price in <paramref name="To"/>.</param>
/// <param name="To">The user's currency.</param>
/// <param name="Rate">One unit of the price's currency in <paramref name="To"/>.</param>
/// <param name="RateDay">The day the rate is from (shown as "rate of 25 October").</param>
public sealed record Conversion(Price From, decimal Amount, string To, decimal Rate, DateOnly RateDay);

/// <summary>
/// Prices in the user's currency (new in Lamha 3): the day's rates from ExchangeRate-API's open access service
/// (open.er-api.com, chosen 2026-10-25; its terms ask for the credit the card shows), fetched at most once a day and
/// kept, so a conversion still works offline with the date of the last rates. Nothing about the user is sent: the
/// request is the same table of rates for everyone.
/// </summary>
public sealed class CurrencyService(Http http, IStateStore state, INetworkStatus network, TimeProvider time, Func<string>? region = null)
{
    public const string RatesUrl = "https://open.er-api.com/v6/latest/USD";

    /// <summary>The credit ExchangeRate-API asks for, in its words, linked to its site.</summary>
    public const string Credit = "Rates By Exchange Rate API";

    public const string CreditUrl = "https://www.exchangerate-api.com";

    private const string RatesKey = "currencyRates";

    /// <summary>The rates are fetched again after this long (the service updates once a day).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Rates? _rates;

    /// <summary>The user's currency: the setting, or Windows' region's ("SAR" in Saudi Arabia).</summary>
    public string Mine(LamhaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Currency.Length == 3)
        {
            return settings.Currency.ToUpperInvariant();
        }

        try
        {
            return region?.Invoke() ?? RegionInfo.CurrentRegion.ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return "USD";
        }
    }

    /// <summary>
    /// <paramref name="price"/> in <paramref name="to"/>, or null when the rates don't have one of the two currencies.
    /// Throws <see cref="ErrorCodes.Network"/> when there are no rates yet and they can't be fetched.
    /// </summary>
    public async Task<Conversion?> ConvertAsync(Price price, string to, CancellationToken cancel = default)
    {
        var rates = await RatesAsync(cancel).ConfigureAwait(false);
        if (!rates.PerDollar.TryGetValue(price.Currency, out var from) || !rates.PerDollar.TryGetValue(to, out var target) || from <= 0)
        {
            return null;
        }

        // The table is per US dollar: through the dollar from one currency to the other.
        var rate = target / from;
        return new Conversion(price, price.Amount * rate, to, rate, DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(rates.Updated).UtcDateTime));
    }

    private async Task<Rates> RatesAsync(CancellationToken cancel)
    {
        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            _rates ??= Rates.Parse(state.Get(RatesKey));
            var now = time.GetUtcNow().ToUnixTimeSeconds();
            if (_rates is not null && (now - _rates.Fetched < MaxAge.TotalSeconds || network.IsOffline))
            {
                return _rates;
            }

            try
            {
                // No retry: the kept rates answer meanwhile, and the next price asks again.
                var json = await http.GetJsonAsync(() => Http.Get(RatesUrl), retries: 0, Timeout, cancel).ConfigureAwait(false);
                if (json?["result"]?.GetValue<string>() != "success" || Rates.From(json, now) is not { } fresh)
                {
                    throw new HttpFailure(HttpFailureKind.Invalid);
                }

                _rates = fresh;
                state.Set(RatesKey, fresh.ToJson());
                return fresh;
            }
            catch (HttpFailure) when (_rates is not null)
            {
                return _rates; // yesterday's rates, with their date, rather than none
            }
            catch (HttpFailure)
            {
                throw new LamhaException(ErrorCodes.Network);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The table as kept: units per US dollar, when the service updated it, and when Lamha fetched it.</summary>
    private sealed record Rates(Dictionary<string, decimal> PerDollar, long Updated, long Fetched)
    {
        public static Rates? From(JsonNode json, long fetched)
        {
            if (json["rates"] is not JsonObject table)
            {
                return null;
            }

            var perDollar = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var (code, value) in table)
            {
                if (value is JsonValue v && v.TryGetValue<decimal>(out var d) && d > 0)
                {
                    perDollar[code] = d;
                }
            }

            var updated = json["time_last_update_unix"] is JsonValue u && u.TryGetValue<long>(out var t) ? t : fetched;
            return perDollar.Count > 0 ? new Rates(perDollar, updated, fetched) : null;
        }

        public static Rates? Parse(string? kept)
        {
            try
            {
                return kept is null || JsonNode.Parse(kept) is not { } json ? null
                    : From(json, json["fetched"] is JsonValue f && f.TryGetValue<long>(out var at) ? at : 0);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        public string ToJson()
        {
            var table = new JsonObject();
            foreach (var (code, value) in PerDollar)
            {
                table[code] = value;
            }

            return new JsonObject { ["rates"] = table, ["time_last_update_unix"] = Updated, ["fetched"] = Fetched }.ToJsonString();
        }
    }
}
