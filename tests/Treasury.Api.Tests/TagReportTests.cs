using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// GET /api/reports/tag/{tagId}: a Tag's Income and Expense totals by Category, in CHF, for one Owner.
public class TagReportTests : ApiTestBase
{
    [Test]
    public async Task A_tags_income_and_expense_totals_by_category_roll_up_through_the_tree()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday" });
        var food = await CategoryIdAsync(anna, "Food");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var eatingOut = await CategoryIdAsync(anna, "Food › Eating out");
        var health = await CategoryIdAsync(anna, "Health");
        await AddAsync(anna, cash, -80m, "2026-07-02", groceries, tagIds: [italy]);
        await AddAsync(anna, cash, -45m, "2026-07-03", eatingOut, tagIds: [italy]);
        await AddAsync(anna, cash, -10m, "2026-07-04", food, tagIds: [italy]);
        await AddAsync(anna, cash, -5m, "2026-07-05", tagIds: [italy]); // Uncategorised
        await AddAsync(anna, cash, 30m, "2026-07-06", tagIds: [italy]); // a refund
        await AddAsync(anna, cash, -999m, "2026-07-03", groceries);    // not tagged
        await AddAsync(anna, cash, -40m, "2026-07-03", health);        // not tagged

        var report = await TagReportAsync(anna, italy);

        await Assert.That(report.Tag.Id).IsEqualTo(italy);
        await Assert.That(report.Tag.Name).IsEqualTo("Italy holiday");
        await Assert.That(report.Currency).IsEqualTo("CHF");
        await Assert.That(report.Owner).IsEqualTo("me");
        await Assert.That(report.Expense.Total).IsEqualTo(140m);
        await Assert.That(report.Expense.Uncategorised).IsEqualTo(5m);
        await Assert.That(report.Expense.Categories.Select(c => (c.Path, c.Total, c.Direct)).ToList()).IsEquivalentTo(new[]
        {
            ("Food", 135m, 10m),
            ("Food › Eating out", 45m, 45m),
            ("Food › Groceries", 80m, 80m),
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(report.Income.Total).IsEqualTo(30m);
        await Assert.That(report.Net).IsEqualTo(-110m);
    }

    [Test]
    public async Task The_owner_filter_switches_between_the_viewers_own_tagged_figures_and_the_familys()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaOwner = await MeAsync(anna);
        var annasCash = await CreateAccountAsync(anna, "Cash", annaOwner);
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday" });
        await AddAsync(anna, annasCash, -10m, "2026-07-02", tagIds: [italy]);                    // Anna's
        await AddAsync(anna, joint, -20m, "2026-07-02", owner: annaOwner, tagIds: [italy]);      // Anna's, Family Account
        await AddAsync(anna, joint, -300m, "2026-07-02", tagIds: [italy]);                       // Family's
        await AddAsync(anna, annasCash, -4000m, "2026-07-02", owner: Family, tagIds: [italy]);   // Family's, Anna's Account

        var mine = await TagReportAsync(anna, italy, "owner=me");
        var family = await TagReportAsync(anna, italy, "owner=family");

        await Assert.That(mine.Owner).IsEqualTo("me");
        await Assert.That(mine.Expense.Total).IsEqualTo(30m);
        await Assert.That(family.Owner).IsEqualTo("family");
        await Assert.That(family.Expense.Total).IsEqualTo(4300m);
        await Assert.That((await TagReportAsync(anna, italy)).Expense.Total).IsEqualTo(30m);
    }

    [Test]
    public async Task Only_transactions_the_viewer_can_see_are_counted()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var bobOwner = await MeAsync(bob);
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var bobsCash = await CreateAccountAsync(bob, "Bob's cash", bobOwner);
        // A date-range Tag reaches every Transaction in range, including Bob's private ones.
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        await AddAsync(anna, joint, -300m, "2026-07-02");                 // Family's
        await AddAsync(anna, joint, -50m, "2026-07-03", owner: bobOwner); // Bob's, in the Family Account
        await AddAsync(bob, bobsCash, -7m, "2026-07-04");                 // Bob's, private

        await Assert.That((await TagReportAsync(anna, italy, "owner=family")).Expense.Total).IsEqualTo(300m);
        await Assert.That((await TagReportAsync(anna, italy, "owner=me")).Expense.Total).IsEqualTo(0m);
        await Assert.That((await TagReportAsync(bob, italy, "owner=family")).Expense.Total).IsEqualTo(300m);
        await Assert.That((await TagReportAsync(bob, italy, "owner=me")).Expense.Total).IsEqualTo(57m);
    }

    [Test]
    public async Task The_admin_role_gives_no_view_into_a_members_tagged_figures()
    {
        await LoginAsAdminAsync();
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday" });
        await AddAsync(anna, cash, -10m, "2026-07-02", tagIds: [italy]);

        await Assert.That((await TagReportAsync(Client, italy, "owner=me")).Expense.Total).IsEqualTo(0m);
        await Assert.That((await TagReportAsync(Client, italy, "owner=family")).Expense.Total).IsEqualTo(0m);
    }

    [Test]
    public async Task A_tag_without_a_date_range_covers_all_time_unless_a_period_is_chosen()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var gifts = await CreateTagAsync(anna, new { name = "gifts" });
        await AddAsync(anna, cash, -10m, "2024-12-20", tagIds: [gifts]);
        await AddAsync(anna, cash, -20m, "2026-03-01", tagIds: [gifts]);
        await AddAsync(anna, cash, -40m, "2026-11-20", tagIds: [gifts]);

        var allTime = await TagReportAsync(anna, gifts);
        var chosen = await TagReportAsync(anna, gifts, "from=2026-01-01&to=2026-03-01");
        var openEnded = await TagReportAsync(anna, gifts, "from=2026-03-02");

        await Assert.That(allTime.From).IsNull();
        await Assert.That(allTime.To).IsNull();
        await Assert.That(allTime.Expense.Total).IsEqualTo(70m);
        await Assert.That(chosen.From).IsEqualTo("2026-01-01");
        await Assert.That(chosen.To).IsEqualTo("2026-03-01");
        await Assert.That(chosen.Expense.Total).IsEqualTo(20m);
        await Assert.That(openEnded.Expense.Total).IsEqualTo(40m);
    }

    [Test]
    public async Task A_date_range_tag_covers_its_range_by_default()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        await AddAsync(anna, cash, -100m, "2026-07-14");                    // tagged by the range
        var early = await AddAsync(anna, cash, -30m, "2026-06-28");         // booked before the trip ...
        await PatchAsync(anna, early, new { addTagIds = new[] { italy } }); // ... and tagged by hand

        var byDefault = await TagReportAsync(anna, italy);
        var widened = await TagReportAsync(anna, italy, "from=2026-06-01&to=2026-07-31");

        await Assert.That(byDefault.Tag.DateFrom).IsEqualTo("2026-07-01");
        await Assert.That(byDefault.From).IsEqualTo("2026-07-01");
        await Assert.That(byDefault.To).IsEqualTo("2026-07-14");
        await Assert.That(byDefault.Expense.Total).IsEqualTo(100m);
        await Assert.That(widened.Expense.Total).IsEqualTo(130m);
        var allTime = await TagReportAsync(anna, italy, "allTime=true");
        await Assert.That(allTime.From).IsNull();
        await Assert.That(allTime.To).IsNull();
        await Assert.That(allTime.Expense.Total).IsEqualTo(130m);
    }

    [Test]
    public async Task Foreign_amounts_are_converted_to_chf_and_those_without_a_rate_yet_are_counted_as_pending()
    {
        App.ExchangeRates.Publish("2026-07-10", chf: 0.9300m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var euros = await CreateAccountAsync(anna, "Euro card", me, "EUR");
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday" });
        var eatingOut = await CategoryIdAsync(anna, "Food › Eating out");
        await AddAsync(anna, euros, -100m, "2026-07-10", eatingOut, tagIds: [italy]); // 93.00 CHF
        await AddAsync(anna, euros, -50m, "2026-07-02", eatingOut, tagIds: [italy]);  // no rate on or before the 2nd

        var report = await EventuallyAsync(anna, italy, r => r.Expense.Total > 0m);

        await Assert.That(report.Expense.Total).IsEqualTo(93m);
        await Assert.That(report.Expense.PendingConversion).IsEqualTo(1);
        await Assert.That(report.Expense.Categories.Single(c => c.Id == eatingOut).Total).IsEqualTo(93m);
    }

    [Test]
    public async Task An_unknown_tag_is_not_found()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.GetAsync($"/api/reports/tag/{Guid.NewGuid()}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("owner=bob", "owner")]
    [Arguments("from=2026-13-01", "from")]
    [Arguments("to=July", "to")]
    [Arguments("from=2026-07-02&to=2026-07-01", "to")]
    public async Task An_invalid_owner_or_period_is_rejected(string query, string field)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var italy = await CreateTagAsync(anna, new { name = "Italy holiday" });

        var res = await anna.GetAsync($"/api/reports/tag/{italy}?{query}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await res.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors.Keys).Contains(field);
    }

    [Test]
    public async Task The_report_requires_login()
    {
        var res = await Client.GetAsync($"/api/reports/tag/{Guid.NewGuid()}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    /// Exchange rates are fetched in the background; polls the report until the condition holds.
    private static async Task<TagReport> EventuallyAsync(HttpClient client, Guid tagId, Func<TagReport, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var report = await TagReportAsync(client, tagId);
            if (done(report)) return report;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met: {report}");
            await Task.Delay(25);
        }
    }

    private static async Task PatchAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PatchAsJsonAsync($"/api/transactions/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Patch failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<TagReport> TagReportAsync(HttpClient client, Guid tagId, string? query = null)
    {
        var res = await client.GetAsync($"/api/reports/tag/{tagId}" + (query is null ? "" : $"?{query}"));
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Report failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<TagReport>())!;
    }

    private static async Task<Guid> CreateTagAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/tags", body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

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

    private static async Task<Guid> AddAsync(HttpClient client, Guid accountId, decimal amount, string date,
        Guid? categoryId = null, object? owner = null, Guid[]? tagIds = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description = "Something", categoryId, owner, tagIds });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!.Single(c => c.Path == path).Id;

    private record Me(Guid Id);

    private record Created(Guid Id);

    private record Category(Guid Id, string Path);

    private record TagSummary(Guid Id, string Name, string? DateFrom, string? DateTo);

    private record TagReport(TagSummary Tag, string Owner, string? From, string? To, string Currency, Section Income,
        Section Expense, decimal Net);

    private record Section(decimal Total, int PendingConversion, decimal Uncategorised, decimal Contributions,
        List<CategoryTotal> Categories);

    private record CategoryTotal(Guid Id, string Name, Guid? ParentId, string Path, string? BuiltInKey,
        decimal Total, decimal Direct);
}
