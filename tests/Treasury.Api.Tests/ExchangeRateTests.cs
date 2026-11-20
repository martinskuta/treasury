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
