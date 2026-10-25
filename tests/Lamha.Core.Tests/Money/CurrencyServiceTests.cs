using Lamha.Core.Money;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Money;

/// <summary>New in Lamha 3: the day's rates, kept for offline, and a price in the user's currency.</summary>
public class CurrencyServiceTests
{
    /// <summary>ExchangeRate-API's answer, cut down (per US dollar), updated 25 October 2026.</summary>
    private const string Table = """
        { "result": "success", "base_code": "USD", "time_last_update_unix": 1792886551,
          "rates": { "USD": 1, "SAR": 3.75, "EUR": 0.92, "EGP": 48.5 } }
        """;

    private static (TestLamha Lamha, CurrencyService Money, List<string> Calls) Make(Func<int, HttpResponseMessage>? reply = null)
    {
        var calls = new List<string>();
        var net = new FakeNet((c, n, _) =>
        {
            lock (calls)
            {
                calls.Add(c.Url);
            }

            return Task.FromResult(reply?.Invoke(n) ?? FakeNet.Json(200, Table));
        });
        var lamha = new TestLamha(net);
        return (lamha, new CurrencyService(lamha.Http, lamha.State, lamha.Network, lamha.Clock, () => "SAR"), calls);
    }

    [Fact]
    public async Task APriceInTheUsersCurrencyWithTheRateAndItsDay()
    {
        var (lamha, money, calls) = Make();
        using var _ = lamha;
        var c = await money.ConvertAsync(new Price(49.99m, "USD"), "SAR", TestContext.Current.CancellationToken);
        Assert.NotNull(c);
        Assert.Equal(187.4625m, c.Amount);
        Assert.Equal(3.75m, c.Rate);
        Assert.Equal(new DateOnly(2026, 10, 25), c.RateDay);
        Assert.Equal([CurrencyService.RatesUrl], calls);

        // Between two currencies, through the dollar.
        var euros = await money.ConvertAsync(new Price(92m, "EUR"), "EGP", TestContext.Current.CancellationToken);
        Assert.Equal(4850m, decimal.Round(euros!.Amount, 6));
    }

    [Fact]
    public async Task TheRatesAreFetchedAtMostOnceADayAndKept()
    {
        var (lamha, money, calls) = Make();
        using var _ = lamha;
        await money.ConvertAsync(new Price(1, "USD"), "SAR", TestContext.Current.CancellationToken);
        lamha.Clock.Advance(TimeSpan.FromHours(23));
        await money.ConvertAsync(new Price(2, "USD"), "SAR", TestContext.Current.CancellationToken);
        Assert.Single(calls);

        // A new start of Lamha reads them from the database.
        var again = new CurrencyService(lamha.Http, lamha.State, lamha.Network, lamha.Clock);
        await again.ConvertAsync(new Price(3, "USD"), "SAR", TestContext.Current.CancellationToken);
        Assert.Single(calls);

        lamha.Clock.Advance(TimeSpan.FromHours(2));
        await money.ConvertAsync(new Price(4, "USD"), "SAR", TestContext.Current.CancellationToken);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task OfflineYesterdaysRatesStillConvertWithTheirDay()
    {
        var (lamha, money, calls) = Make(n => n == 1 ? FakeNet.Json(200, Table) : FakeNet.Json(503, "{}"));
        using var _ = lamha;
        await money.ConvertAsync(new Price(1, "USD"), "SAR", TestContext.Current.CancellationToken);
        lamha.Clock.Advance(TimeSpan.FromDays(3));

        // The service is down: the kept rates answer.
        var c = await money.ConvertAsync(new Price(10, "USD"), "SAR", TestContext.Current.CancellationToken);
        Assert.Equal(37.5m, c!.Amount);
        Assert.Equal(new DateOnly(2026, 10, 25), c.RateDay);

        // Windows says there's no network: not even asked.
        lamha.Network.IsOffline = true;
        var asked = calls.Count;
        await money.ConvertAsync(new Price(10, "USD"), "SAR", TestContext.Current.CancellationToken);
        Assert.Equal(asked, calls.Count);
    }

    [Fact]
    public async Task NoRatesAtAllIsANetworkError()
    {
        var (lamha, money, _) = Make(_ => throw FakeNet.Offline());
        using var __ = lamha;
        var e = await Assert.ThrowsAsync<LamhaException>(() => money.ConvertAsync(new Price(1, "USD"), "SAR", TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Network, e.Code);
    }

    [Fact]
    public async Task ACurrencyTheRatesDontHaveIsNoConversion()
    {
        var (lamha, money, _) = Make();
        using var _ = lamha;
        Assert.Null(await money.ConvertAsync(new Price(1, "XYZ"), "SAR", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheUsersCurrencyIsTheSettingOrWindowsRegion()
    {
        var (lamha, money, _) = Make();
        using var _ = lamha;
        Assert.Equal("SAR", money.Mine(new LamhaSettings()));
        Assert.Equal("EGP", money.Mine(new LamhaSettings { Currency = "egp" }));
    }
}
