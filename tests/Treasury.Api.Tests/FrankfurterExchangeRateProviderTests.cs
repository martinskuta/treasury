using System.Net;
using System.Text;
using Treasury.Api.ExchangeRates;

namespace Treasury.Api.Tests;

/// The Frankfurter adapter against canned responses (recorded from api.frankfurter.dev); no network.
public class FrankfurterExchangeRateProviderTests
{
    [Test]
    public async Task Reads_the_euro_based_rates_of_each_day_in_the_range()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"amount":1.0,"base":"EUR","start_date":"2026-11-02","end_date":"2026-11-03",
             "rates":{"2026-11-02":{"CHF":0.9193,"CZK":24.351,"USD":1.1216},
                      "2026-11-03":{"CHF":0.9166,"CZK":24.343,"USD":1.1179}}}
            """);
        var provider = new FrankfurterExchangeRateProvider(
            new HttpClient(handler) { BaseAddress = FrankfurterExchangeRateProvider.BaseAddress });

        var days = await provider.GetDailyRatesAsync(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), default);

        await Assert.That(handler.RequestedUri!.ToString())
            .IsEqualTo("https://api.frankfurter.dev/v1/2026-11-02..2026-11-03?base=EUR&symbols=" +
                "AUD,BGN,BRL,CAD,CHF,CNY,CZK,DKK,GBP,HKD,HRK,HUF,IDR,ILS,INR,ISK,JPY,KRW,MXN,MYR,NOK,NZD,PHP,PLN,RON,SEK,SGD," +
                "THB,TRY,USD,ZAR");
        await Assert.That(days.Select(d => d.Date))
            .IsEquivalentTo([new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)]);
        await Assert.That(days[1].PerEur["CHF"]).IsEqualTo(0.9166m);
        await Assert.That(days[1].PerEur["CZK"]).IsEqualTo(24.343m);
        await Assert.That(days[1].PerEur["USD"]).IsEqualTo(1.1179m);
    }

    [Test]
    public async Task Not_found_means_nothing_published_yet()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"message":"not found"}""");
        var provider = new FrankfurterExchangeRateProvider(
            new HttpClient(handler) { BaseAddress = FrankfurterExchangeRateProvider.BaseAddress });

        var days = await provider.GetDailyRatesAsync(new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22), default);

        await Assert.That(days).IsEmpty();
    }

    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
