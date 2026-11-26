using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Exchange rates come from the fake provider (App.ExchangeRates); the clock is App.Clock (2026-11-20 09:00 UTC).
/// Fetching runs in the background, so assertions on converted amounts wait for it (EventuallyAsync).
public class ExchangeRateTests : ApiTestBase
{
    [Test]
    public async Task A_euro_transaction_shows_its_chf_equivalent_at_the_rate_of_its_date()
    {
        App.ExchangeRates.Publish("2026-11-17", chf: 0.9300m);
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");

        await AddAsync(anna, euros, -100m, "2026-11-18");

        var t = await EventuallyAsync(anna, list => list.Single().ChfAmount is not null);
        await Assert.That(t.Single().Currency).IsEqualTo("EUR");
        await Assert.That(t.Single().ChfAmount).IsEqualTo(-92.66m);
    }

    [Test]
    public async Task A_weekend_transaction_uses_the_most_recent_earlier_rate()
    {
        App.ExchangeRates.Publish("2026-11-13", chf: 0.9266m); // Friday
        App.ExchangeRates.Publish("2026-11-16", chf: 0.9500m); // Monday
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");

        await AddAsync(anna, euros, -100m, "2026-11-15"); // Sunday

        var list = await EventuallyAsync(anna, l => l.Single().ChfAmount is not null);
        await Assert.That(list.Single().ChfAmount).IsEqualTo(-92.66m);
    }

    [Test]
    public async Task Koruna_and_dollar_amounts_are_converted_through_the_euro_rates()
    {
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m, czk: 24.424m, usd: 1.1052m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var korunas = await CreateAccountAsync(anna, "Koruna account", "CZK");
        var dollars = await CreateAccountAsync(anna, "Dollar account", "USD");

        await AddAsync(anna, korunas, -1000m, "2026-11-18");
        await AddAsync(anna, dollars, 250m, "2026-11-18");

        var list = await EventuallyAsync(anna, l => l.All(t => t.ChfAmount is not null));
        // 1000 CZK / 24.424 CZK per EUR * 0.9266 CHF per EUR = 37.938 CHF; 250 USD / 1.1052 * 0.9266 = 209.600 CHF
        await Assert.That(list.Single(t => t.Currency == "CZK").ChfAmount).IsEqualTo(-37.94m);
        await Assert.That(list.Single(t => t.Currency == "USD").ChfAmount).IsEqualTo(209.60m);
    }

    [Test]
    public async Task A_chf_transaction_is_its_own_chf_equivalent_without_any_rate()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var francs = await CreateAccountAsync(anna, "Cash", "CHF");

        var t = await AddAsync(anna, francs, -12.50m, "2026-11-18");

        await Assert.That(t.ChfAmount).IsEqualTo(-12.50m);
    }

    [Test]
    public async Task Without_a_rate_on_or_before_its_date_a_transaction_has_no_chf_equivalent_yet()
    {
        App.ExchangeRates.Publish("2026-11-19", chf: 0.93m); // only after the Transaction's date
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");

        await AddAsync(anna, euros, -100m, "2026-11-18");
        await EventuallyAsync(anna, _ => App.ExchangeRates.Requests.Count >= 2); // startup run + the backfill request

        var t = (await anna.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!.Single();
        await Assert.That(t.ChfAmount).IsNull();
    }

    [Test]
    public async Task The_scheduled_job_fetches_each_new_days_rates()
    {
        App.ExchangeRates.Publish("2026-11-19", chf: 0.9300m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");
        await AddAsync(anna, euros, -100m, "2026-11-20");
        await EventuallyAsync(anna, l => l.Single().ChfAmount == -93.00m); // Thursday's rate until Friday's is out

        App.ExchangeRates.Publish("2026-11-20", chf: 0.9266m); // ECB publishes Friday's rates in the afternoon
        // Time passes (an hour per poll, so the job's next run comes due however its timer races the test).
        var list = await EventuallyAsync(anna, l => l.Single().ChfAmount == -92.66m,
            betweenPolls: () => App.Clock.Advance(TimeSpan.FromHours(1)));
        await Assert.That(list.Single().ChfAmount).IsEqualTo(-92.66m);
    }

    [Test]
    public async Task Rates_are_backfilled_for_a_transaction_dated_before_every_stored_rate()
    {
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m);
        App.ExchangeRates.Publish("2025-06-02", chf: 0.9400m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");
        await AddAsync(anna, euros, -100m, "2026-11-18");
        await EventuallyAsync(anna, l => l.Single().ChfAmount is not null);

        await AddAsync(anna, euros, -50m, "2025-06-03"); // long before the Account's opening date

        var list = await EventuallyAsync(anna, l => l.All(t => t.ChfAmount is not null));
        await Assert.That(list.Single(t => t.Date == new DateOnly(2025, 6, 3)).ChfAmount).IsEqualTo(-47.00m);
        await Assert.That(list.Single(t => t.Date == new DateOnly(2026, 11, 18)).ChfAmount).IsEqualTo(-92.66m);
    }

    [Test]
    public async Task A_newly_supported_currency_gets_its_rates_backfilled_over_days_other_currencies_already_have()
    {
        // The provider serves CHF/CZK/USD only, like before Treasury supported more currencies.
        App.ExchangeRates.Publish("2026-03-02", chf: 0.9500m);
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9300m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");
        await AddAsync(anna, euros, -100m, "2026-03-03");
        await EventuallyAsync(anna, l => l.Single().ChfAmount == -95.00m,
            betweenPolls: () => App.Clock.Advance(TimeSpan.FromHours(1)));
        var pounds = await CreateAccountAsync(anna, "Pound card", "GBP");
        await AddAsync(anna, pounds, -100m, "2026-03-03");

        // Now pound rates are served too, also for the days whose CHF/CZK/USD rates are already stored.
        App.ExchangeRates.Publish("2026-03-02", "GBP", 0.8000m);
        App.ExchangeRates.Publish("2026-11-18", "GBP", 0.8100m);

        // 100 GBP = 125 EUR = 118.75 CHF at 2026-03-02's rates.
        var list = await EventuallyAsync(anna, l => l.All(t => t.ChfAmount is not null),
            betweenPolls: () => App.Clock.Advance(TimeSpan.FromHours(1)));
        await Assert.That(list.Single(t => t.Currency == "GBP").ChfAmount).IsEqualTo(-118.75m);
    }

    [Test]
    public async Task Moving_a_transaction_to_an_earlier_date_backfills_its_rate()
    {
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m);
        App.ExchangeRates.Publish("2025-03-03", chf: 0.9500m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", "EUR");
        var t = await AddAsync(anna, euros, -100m, "2026-11-18");
        await EventuallyAsync(anna, l => l.Single().ChfAmount is not null);

        var res = await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { date = "2025-03-04" });
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var list = await EventuallyAsync(anna, l => l.Single().ChfAmount == -95.00m);
        await Assert.That(list.Single().ChfAmount).IsEqualTo(-95.00m);
    }

    [Test]
    public async Task A_kuna_amount_converts_at_the_ecb_rate_before_the_euro_and_at_the_fixed_rate_after()
    {
        App.ExchangeRates.Publish("2022-06-01", chf: 1.0300m);
        App.ExchangeRates.Publish("2022-06-01", "HRK", 7.5000m);
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9300m); // no HRK: the ECB stopped publishing it in 2023
        using var anna = await LoginAsNewMemberAsync("anna");
        var kunas = await CreateAccountAsync(anna, "Revolut HRK", "HRK", openingDate: "2022-05-01");

        await AddAsync(anna, kunas, -750m, "2022-06-02");
        await AddAsync(anna, kunas, -753.45m, "2026-11-18");

        var list = await EventuallyAsync(anna, l => l.All(t => t.ChfAmount is not null),
            betweenPolls: () => App.Clock.Advance(TimeSpan.FromHours(1)));
        // 750 HRK / 7.5 * 1.03 = 103.00 CHF; 753.45 HRK / 7.5345 (fixed) * 0.93 = 93.00 CHF
        await Assert.That(list.Single(t => t.Date.Year == 2022).ChfAmount).IsEqualTo(-103.00m);
        await Assert.That(list.Single(t => t.Date.Year == 2026).ChfAmount).IsEqualTo(-93.00m);
    }

    [Test]
    public async Task A_retired_currency_is_not_asked_for_after_the_euro_replaced_it()
    {
        // Every currency has its rates, so nothing but a retired currency's missing days could trigger a fetch.
        PublishEveryCurrency("2022-12-30", except: []);
        PublishEveryCurrency("2025-12-31", except: ["HRK"]);
        PublishEveryCurrency("2026-11-20", except: ["HRK", "BGN"]);
        using var anna = await LoginAsNewMemberAsync("anna");
        var kunas = await CreateAccountAsync(anna, "Revolut HRK", "HRK", openingDate: "2022-12-30");
        await AddAsync(anna, kunas, -100m, "2022-12-30");
        await EventuallyAsync(anna, l => l.Single().ChfAmount is not null);
        var settled = App.ExchangeRates.Requests.Count;

        // A few more scheduled runs on the same day: nothing is missing, so nothing is asked for.
        for (var i = 0; i < 5; i++)
        {
            App.Clock.Advance(TimeSpan.FromMinutes(70));
            await Task.Delay(100);
        }

        await Assert.That(App.ExchangeRates.Requests.Skip(settled).Where(r => r.From < new DateOnly(2026, 1, 1)))
            .IsEmpty();
    }

    private void PublishEveryCurrency(string date, string[] except)
    {
        App.ExchangeRates.Publish(date, chf: 0.95m);
        foreach (var currency in Treasury.Api.ExchangeRates.ExchangeRate.FetchedCurrencies.Except(except))
            App.ExchangeRates.Publish(date, currency, 2m);
    }

    // ---- helpers ----

    /// Polls GET /api/transactions until the condition holds (background fetching), failing after a few seconds.
    private static async Task<List<Transaction>> EventuallyAsync(HttpClient client, Func<List<Transaction>, bool> done,
        Action? betweenPolls = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var list = (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;
            if (done(list)) return list;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met: " + string.Join(", ",
                    list.Select(t => $"{t.Date} {t.Amount} {t.Currency} -> {t.ChfAmount?.ToString() ?? "null"}")));
            await Task.Delay(25);
            betweenPolls?.Invoke();
        }
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name, string currency,
        string openingDate = "2026-01-01")
    {
        var me = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!;
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name,
            owner = new { kind = "member", memberId = me.Id },
            integrationKey = "test",
            currency,
            openingBalance = 0m,
            openingDate,
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<IdOnly>())!.Id;
    }

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount, string date)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description = "Something" });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private record Me(Guid Id);

    private record IdOnly(Guid Id);

    private record Transaction(Guid Id, DateOnly Date, decimal Amount, string Currency, decimal? ChfAmount);
}
