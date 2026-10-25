using Lamha.Core.Money;

namespace Lamha.Core.Tests.Money;

/// <summary>New in Lamha 3: a selected price, for "prices in your currency" (phase 1's card design).</summary>
public class PricesTests
{
    [Theory]
    [InlineData("$49.99", 49.99, "USD")]
    [InlineData(" €20 ", 20, "EUR")]
    [InlineData("£5.50", 5.5, "GBP")]
    [InlineData("1,200 SAR", 1200, "SAR")]
    [InlineData("SAR 1,200", 1200, "SAR")]
    [InlineData("1,200.50 sar", 1200.5, "SAR")]
    [InlineData("20,50 €", 20.5, "EUR")]
    [InlineData("1.299,99 €", 1299.99, "EUR")]
    [InlineData("US$ 10", 10, "USD")]
    [InlineData("C$15", 15, "CAD")]
    [InlineData("20 dollars", 20, "USD")]
    [InlineData("٥٠ ريال سعودي", 50, "SAR")]
    [InlineData("١٨٧٫٤٦ ر.س", 187.46, "SAR")]
    [InlineData("١٬٢٠٠ جنيه", 1200, "EGP")]
    [InlineData("300 درهم إماراتي", 300, "AED")]
    [InlineData("¥3000", 3000, "JPY")]
    public void FindsThePrice(string text, double amount, string currency) =>
        Assert.Equal(new Price((decimal)amount, currency), Prices.Find(text, "SAR"));

    [Theory]
    [InlineData("50 ريال", "QAR", "QAR")]
    [InlineData("50 ريال", "EGP", "SAR")]
    [InlineData("10 دينار", "JOD", "JOD")]
    [InlineData("10 دينار", "SAR", "KWD")]
    [InlineData("10 dirhams", "MAD", "MAD")]
    public void ANameCountriesShareIsTheUsersOwnWhenItCanBe(string text, string mine, string currency) =>
        Assert.Equal(currency, Prices.Find(text, mine)?.Currency);

    [Theory]
    [InlineData("49.99")]
    [InlineData("$")]
    [InlineData("settle")]
    [InlineData("The price is $49.99 today, down from $79.99 last week.")]
    [InlineData("$49.99 USD")]
    [InlineData("THE 5")]
    [InlineData("$0")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyAPriceAloneIsOne(string? text) => Assert.Null(Prices.Find(text, "SAR"));
}
