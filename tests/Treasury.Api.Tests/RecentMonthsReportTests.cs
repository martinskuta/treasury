using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// GET /api/reports/recent-months: the end month of an Owner view and the six months ending at it, each with its
/// Income, Expense and net in CHF. The clock stands at 2026-11-20, so the last complete month is 2026-10.
public class RecentMonthsReportTests : ApiTestBase
{
    [Test]
    public async Task The_end_month_is_the_month_of_the_latest_transaction_and_six_months_end_at_it_oldest_first()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, 5000m, "2026-09-25");
        await AddAsync(anna, cash, -120.50m, "2026-09-30");
        await AddAsync(anna, cash, -80m, "2026-07-01");
        await AddAsync(anna, cash, -999m, "2026-03-31"); // before the six months

        var report = await RecentMonthsAsync(anna, "me");

        await Assert.That(report.Owner).IsEqualTo("me");
        await Assert.That(report.Currency).IsEqualTo("CHF");
        await Assert.That(report.EndMonth).IsEqualTo("2026-09");
        await Assert.That(report.Months.Select(m => (m.Month, m.Income, m.Expense, m.Net)).ToList()).IsEquivalentTo(new[]
        {
            ("2026-04", 0m, 0m, 0m),
            ("2026-05", 0m, 0m, 0m),
            ("2026-06", 0m, 0m, 0m),
            ("2026-07", 0m, 80m, -80m),
            ("2026-08", 0m, 0m, 0m),
            ("2026-09", 5000m, 120.50m, 4879.50m),
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("2026-11-01")] // the current month
    [Arguments("2026-11-20")] // today
    [Arguments("2027-02-14")] // the future
    public async Task A_transaction_in_the_current_month_or_the_future_does_not_push_the_end_month_past_the_last_complete_month(
        string date)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, -10m, "2026-08-15");
        await AddAsync(anna, cash, -70m, date);

        var report = await RecentMonthsAsync(anna, "me");

        await Assert.That(report.EndMonth).IsEqualTo("2026-10");
        await Assert.That(report.Months.Select(m => m.Month).ToList()).IsEquivalentTo(
            ["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(report.Months.Sum(m => m.Expense)).IsEqualTo(10m);
    }

    [Test]
    public async Task The_last_complete_month_follows_the_clock()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, -70m, "2026-11-10");

        await Assert.That((await RecentMonthsAsync(anna, "me")).EndMonth).IsEqualTo("2026-10");

        App.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        var december = await RecentMonthsAsync(anna, "me");

        await Assert.That(december.EndMonth).IsEqualTo("2026-11");
        await Assert.That(december.Months[^1].Expense).IsEqualTo(70m);
    }

    [Test]
    public async Task With_unimported_months_the_six_months_end_at_the_latest_month_that_has_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, -40m, "2026-06-30"); // nothing for July to October yet
        await AddAsync(anna, cash, 300m, "2026-01-01");

        var report = await RecentMonthsAsync(anna, "me");

        await Assert.That(report.EndMonth).IsEqualTo("2026-06");
        await Assert.That(report.Months.Select(m => (m.Month, m.Income, m.Expense)).ToList()).IsEquivalentTo(new[]
        {
            ("2026-01", 300m, 0m),
            ("2026-02", 0m, 0m),
            ("2026-03", 0m, 0m),
            ("2026-04", 0m, 0m),
            ("2026-05", 0m, 0m),
            ("2026-06", 0m, 40m),
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Without_transactions_the_six_months_end_at_the_last_complete_month_and_are_empty()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var report = await RecentMonthsAsync(anna, null);

        await Assert.That(report.Owner).IsEqualTo("me");
        await Assert.That(report.EndMonth).IsEqualTo("2026-10");
        await Assert.That(report.Months.Select(m => (m.Month, m.Income, m.Expense, m.Net, m.PendingConversion)).ToList())
            .IsEquivalentTo(new[]
            {
                ("2026-05", 0m, 0m, 0m, 0),
                ("2026-06", 0m, 0m, 0m, 0),
                ("2026-07", 0m, 0m, 0m, 0),
                ("2026-08", 0m, 0m, 0m, 0),
                ("2026-09", 0m, 0m, 0m, 0),
                ("2026-10", 0m, 0m, 0m, 0),
            }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Mine_and_family_each_end_at_their_own_latest_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annaOwner = await MeAsync(anna);
        var annasCash = await CreateAccountAsync(anna, "Cash", annaOwner);
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var bobsCash = await CreateAccountAsync(bob, "Bob's cash", await MeAsync(bob));
        await AddAsync(anna, annasCash, -10m, "2026-08-02");                    // Anna's
        await AddAsync(anna, joint, -20m, "2026-09-02", owner: annaOwner);      // Anna's, in the Family Account
        await AddAsync(anna, joint, -300m, "2026-06-02");                       // Family's
        await AddAsync(anna, annasCash, -4000m, "2026-07-02", owner: Family);   // Family's, paid from Anna's Account
        await AddAsync(bob, bobsCash, -7m, "2026-10-02");                       // Bob's, private

        var mine = await RecentMonthsAsync(anna, "me");
        var family = await RecentMonthsAsync(anna, "family");

        await Assert.That(mine.EndMonth).IsEqualTo("2026-09");
        await Assert.That(mine.Months[^1].Expense).IsEqualTo(20m);
        await Assert.That(mine.Months.Sum(m => m.Expense)).IsEqualTo(30m);
        await Assert.That(family.Owner).IsEqualTo("family");
        await Assert.That(family.EndMonth).IsEqualTo("2026-07");
        await Assert.That(family.Months[^1].Expense).IsEqualTo(4000m);
        await Assert.That(family.Months.Sum(m => m.Expense)).IsEqualTo(4300m);
        // Bob's private Transaction moves only his own view.
        await Assert.That((await RecentMonthsAsync(bob, "me")).EndMonth).IsEqualTo("2026-10");
        await Assert.That((await RecentMonthsAsync(bob, "family")).EndMonth).IsEqualTo("2026-07");
    }

    [Test]
    public async Task Imported_and_manual_transactions_both_count_towards_the_end_month()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var card = await CreateAccountAsync(anna, "Card", me);
        var cash = await CreateAccountAsync(anna, "Cash", me);
        await ImportAsync(anna, card, new TestStatement().Row("2026-07-15", -45m, "MIGROS"));

        var imported = await RecentMonthsAsync(anna, "me");
        await AddAsync(anna, cash, -5m, "2026-09-09"); // kept by hand
        var withManual = await RecentMonthsAsync(anna, "me");

        await Assert.That(imported.EndMonth).IsEqualTo("2026-07");
        await Assert.That(imported.Months[^1].Expense).IsEqualTo(45m);
        await Assert.That(withManual.EndMonth).IsEqualTo("2026-09");
        await Assert.That(withManual.Months.Select(m => m.Expense).ToList()).IsEquivalentTo(
            [0m, 0m, 0m, 45m, 0m, 5m], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Each_months_totals_agree_with_the_monthly_overview()
    {
        App.ExchangeRates.Publish("2026-09-17", chf: 0.9300m);
        App.ExchangeRates.Publish("2026-09-18", chf: 0.9266m, czk: 24.424m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var francs = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var euros = await CreateAccountAsync(anna, "Euro card", me, "EUR");
        var korunas = await CreateAccountAsync(anna, "Koruna account", me, "CZK");
        await AddAsync(anna, francs, -10m, "2026-09-18");
        await AddAsync(anna, euros, -100m, "2026-09-18");    // 92.66 CHF
        await AddAsync(anna, euros, -100m, "2026-09-17");    // 93.00 CHF
        await AddAsync(anna, korunas, 1000m, "2026-09-18");  // 37.94 CHF
        await AddAsync(anna, euros, -50m, "2026-08-02");     // no rate on or before that day: pending
        await AddAsync(anna, francs, -33m, "2026-08-03");
        await AddAsync(anna, francs, 5000m, "2026-06-25");
        await AddAsync(anna, francs, -1000m, "2026-06-10");  // to savings ...
        await AddAsync(anna, savings, 1000m, "2026-06-11");  // ... matched as a Transfer: counts on neither side

        // Exchange rates are fetched in the background.
        var report = await EventuallyAsync(anna, "me", r => r.Months[^1].PendingConversion == 0);

        await Assert.That(report.EndMonth).IsEqualTo("2026-09");
        await Assert.That(report.Months.Select(m => (m.Month, m.Income, m.Expense, m.Net, m.PendingConversion)).ToList())
            .IsEquivalentTo(new[]
            {
                ("2026-04", 0m, 0m, 0m, 0),
                ("2026-05", 0m, 0m, 0m, 0),
                ("2026-06", 5000m, 0m, 5000m, 0),
                ("2026-07", 0m, 0m, 0m, 0),
                ("2026-08", 0m, 33m, -33m, 1),
                ("2026-09", 37.94m, 195.66m, -157.72m, 0),
            }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        foreach (var month in report.Months)
        {
            var overview = (await anna.GetFromJsonAsync<MonthlyReport>($"/api/reports/monthly?month={month.Month}&owner=me"))!;
            await Assert.That((month.Income, month.Expense, month.Net, month.PendingConversion)).IsEqualTo(
                (overview.Income.Total, overview.Expense.Total, overview.Net,
                    overview.Income.PendingConversion + overview.Expense.PendingConversion));
        }
    }

    [Test]
    public async Task An_invalid_owner_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.GetAsync("/api/reports/recent-months?owner=bob");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await res.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors.Keys).Contains("owner");
    }

    [Test]
    public async Task The_report_requires_login()
    {
        var res = await Client.GetAsync("/api/reports/recent-months");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private static async Task<RecentMonthsReport> EventuallyAsync(HttpClient client, string owner,
        Func<RecentMonthsReport, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var report = await RecentMonthsAsync(client, owner);
            if (done(report)) return report;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met: {report}");
            await Task.Delay(25);
        }
    }

    private static async Task<RecentMonthsReport> RecentMonthsAsync(HttpClient client, string? owner)
    {
        var res = await client.GetAsync("/api/reports/recent-months" + (owner is null ? "" : $"?owner={owner}"));
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Report failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<RecentMonthsReport>())!;
    }

    private static readonly object Family = new { kind = "family" };

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name, object owner, string currency = "CHF")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey = "test", currency, openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task AddAsync(HttpClient client, Guid accountId, decimal amount, string date, object? owner = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description = "Something", owner });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task ImportAsync(HttpClient client, Guid accountId, TestStatement statement)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(statement.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "statement.txt");
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Import failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private record Me(Guid Id);

    private record Created(Guid Id);

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record RecentMonthsReport(string Owner, string Currency, string EndMonth, List<MonthTotals> Months);

    private record MonthTotals(string Month, decimal Income, decimal Expense, decimal Net, int PendingConversion);

    private record MonthlyReport(Section Income, Section Expense, decimal Net);

    private record Section(decimal Total, int PendingConversion);
}
