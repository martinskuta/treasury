using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// GET /api/reports/monthly: Income versus Expense and Expenses by Category, in CHF, for one month and Owner.
public class MonthlyOverviewTests : ApiTestBase
{
    [Test]
    public async Task The_months_income_and_expense_totals_are_shown_in_chf()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, -25.50m, "2026-11-03");
        await AddAsync(anna, cash, -74.50m, "2026-11-30");
        await AddAsync(anna, cash, 5000m, "2026-11-25");
        await AddAsync(anna, cash, -999m, "2026-10-31"); // another month
        await AddAsync(anna, cash, 999m, "2026-12-01");

        var report = await MonthlyAsync(anna, "2026-11", "me");

        await Assert.That(report.Month).IsEqualTo("2026-11");
        await Assert.That(report.Currency).IsEqualTo("CHF");
        await Assert.That(report.Income.Total).IsEqualTo(5000m);
        await Assert.That(report.Expense.Total).IsEqualTo(100m);
        await Assert.That(report.Net).IsEqualTo(4900m);
    }

    [Test]
    public async Task Expenses_by_category_roll_up_through_the_tree_and_each_level_can_be_drilled_into()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var food = await CategoryIdAsync(anna, "Food");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var eatingOut = await CategoryIdAsync(anna, "Food › Eating out");
        var restaurants = await CreateCategoryAsync(anna, "Restaurants", eatingOut);
        var health = await CategoryIdAsync(anna, "Health");
        var salary = await CategoryIdAsync(anna, "Income › Salary");
        await AddAsync(anna, cash, -80m, "2026-11-02", groceries);
        await AddAsync(anna, cash, -25m, "2026-11-03", eatingOut);
        await AddAsync(anna, cash, -15m, "2026-11-04", restaurants);
        await AddAsync(anna, cash, -10m, "2026-11-05", food);
        await AddAsync(anna, cash, -40m, "2026-11-06", health);
        await AddAsync(anna, cash, -5m, "2026-11-07"); // Uncategorised
        await AddAsync(anna, cash, 5000m, "2026-11-25", salary);

        var expense = (await MonthlyAsync(anna, "2026-11", "me")).Expense;

        await Assert.That(expense.Total).IsEqualTo(175m);
        await Assert.That(expense.Uncategorised).IsEqualTo(5m);
        // Tree order: each Category followed by its subtree; only Categories with amounts.
        await Assert.That(expense.Categories.Select(c => (c.Path, c.Total, c.Direct)).ToList()).IsEquivalentTo(new[]
        {
            ("Food", 130m, 10m),
            ("Food › Eating out", 40m, 25m),
            ("Food › Eating out › Restaurants", 15m, 15m),
            ("Food › Groceries", 80m, 80m),
            ("Health", 40m, 40m),
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        // Drilling in: a level's children are the entries whose parentId is that Category.
        await Assert.That(expense.Categories.Where(c => c.ParentId == food).Select(c => c.Name))
            .IsEquivalentTo(["Eating out", "Groceries"]);
        await Assert.That(expense.Categories.Where(c => c.ParentId == null).Select(c => c.Id))
            .IsEquivalentTo([food, health]);
        await Assert.That(expense.Categories.Single(c => c.Id == restaurants).ParentId).IsEqualTo(eatingOut);
    }

    [Test]
    public async Task Income_is_broken_down_by_category_too()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var salary = await CategoryIdAsync(anna, "Income › Salary");
        await AddAsync(anna, cash, 5000m, "2026-11-25", salary);
        await AddAsync(anna, cash, 20m, "2026-11-26");

        var income = (await MonthlyAsync(anna, "2026-11", "me")).Income;

        await Assert.That(income.Total).IsEqualTo(5020m);
        await Assert.That(income.Uncategorised).IsEqualTo(20m);
        await Assert.That(income.Categories.Select(c => (c.Path, c.Total)).ToList())
            .IsEquivalentTo(new[] { ("Income", 5000m), ("Income › Salary", 5000m) });
    }

    [Test]
    public async Task The_owner_filter_switches_between_the_viewers_own_figures_and_the_familys()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaOwner = await MeAsync(anna);
        var annasCash = await CreateAccountAsync(anna, "Cash", annaOwner);
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        await AddAsync(anna, annasCash, -10m, "2026-11-02");                 // Anna's
        await AddAsync(anna, joint, -20m, "2026-11-02", owner: annaOwner);   // Anna's, in the Family Account
        await AddAsync(anna, joint, -300m, "2026-11-02");                    // Family's
        await AddAsync(anna, annasCash, -4000m, "2026-11-02", owner: Family); // Family's, paid from Anna's Account

        var mine = await MonthlyAsync(anna, "2026-11", "me");
        var family = await MonthlyAsync(anna, "2026-11", "family");

        await Assert.That(mine.Owner).IsEqualTo("me");
        await Assert.That(mine.Expense.Total).IsEqualTo(30m);
        await Assert.That(family.Owner).IsEqualTo("family");
        await Assert.That(family.Expense.Total).IsEqualTo(4300m);
        // Without an owner the report shows the viewer's own figures.
        await Assert.That((await MonthlyAsync(anna, "2026-11", null)).Expense.Total).IsEqualTo(30m);
    }

    [Test]
    public async Task Family_figures_include_only_family_owned_transactions_and_nobody_sees_another_members_figures()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var bobOwner = await MeAsync(bob);
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var bobsCash = await CreateAccountAsync(bob, "Bob's cash", bobOwner);
        await AddAsync(anna, joint, -300m, "2026-11-02");                  // Family's
        await AddAsync(anna, joint, -50m, "2026-11-02", owner: bobOwner);  // Bob's, in the Family Account
        await AddAsync(bob, bobsCash, -7m, "2026-11-02");                  // Bob's, private

        var annasFamily = await MonthlyAsync(anna, "2026-11", "family");
        var annasOwn = await MonthlyAsync(anna, "2026-11", "me");
        var bobsFamily = await MonthlyAsync(bob, "2026-11", "family");
        var bobsOwn = await MonthlyAsync(bob, "2026-11", "me");

        await Assert.That(annasFamily.Expense.Total).IsEqualTo(300m);
        await Assert.That(bobsFamily.Expense.Total).IsEqualTo(300m);
        await Assert.That(annasOwn.Expense.Total).IsEqualTo(0m);
        await Assert.That(bobsOwn.Expense.Total).IsEqualTo(57m);
    }

    [Test]
    public async Task The_admin_role_gives_no_view_into_a_members_figures()
    {
        await LoginAsAdminAsync();
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, -10m, "2026-11-02");

        await Assert.That((await MonthlyAsync(Client, "2026-11", "me")).Expense.Total).IsEqualTo(0m);
        await Assert.That((await MonthlyAsync(Client, "2026-11", "family")).Expense.Total).IsEqualTo(0m);
    }

    [Test]
    public async Task Contributions_appear_under_their_categories_and_can_be_left_out_of_real_spending()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var toFamily = await CategoryIdAsync(anna, "Contributions › To Family");
        var fromMembers = await CategoryIdAsync(anna, "Contributions › From Members");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        await AddAsync(anna, cash, -1000m, "2026-11-01", toFamily);
        await AddAsync(anna, cash, -80m, "2026-11-02", groceries);
        await AddAsync(anna, cash, 200m, "2026-11-03", fromMembers);

        var report = await MonthlyAsync(anna, "2026-11", "me");

        await Assert.That(report.Expense.Total).IsEqualTo(1080m);
        await Assert.That(report.Expense.Contributions).IsEqualTo(1000m);
        var contributions = report.Expense.Categories.Single(c => c.Path == "Contributions");
        await Assert.That(contributions.BuiltInKey).IsEqualTo("contributions");
        await Assert.That(contributions.Total).IsEqualTo(1000m);
        await Assert.That(report.Expense.Categories.Single(c => c.Id == toFamily).BuiltInKey)
            .IsEqualTo("contributions.to-family");
        await Assert.That(report.Income.Contributions).IsEqualTo(200m);
        await Assert.That(report.Income.Categories.Single(c => c.Id == fromMembers).Total).IsEqualTo(200m);
    }

    [Test]
    public async Task Amounts_in_other_currencies_are_converted_to_chf_at_each_transactions_date()
    {
        App.ExchangeRates.Publish("2026-11-17", chf: 0.9300m);
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m, czk: 24.424m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var francs = await CreateAccountAsync(anna, "Cash", me);
        var euros = await CreateAccountAsync(anna, "Euro card", me, "EUR");
        var korunas = await CreateAccountAsync(anna, "Koruna account", me, "CZK");
        await AddAsync(anna, francs, -10m, "2026-11-18");
        await AddAsync(anna, euros, -100m, "2026-11-18");   // 92.66 CHF
        await AddAsync(anna, euros, -100m, "2026-11-17");   // 93.00 CHF
        await AddAsync(anna, korunas, 1000m, "2026-11-18"); // 1000 / 24.424 * 0.9266 = 37.94 CHF

        var report = await EventuallyAsync(anna, "2026-11", r => r.Expense.PendingConversion == 0
            && r.Income.PendingConversion == 0);

        await Assert.That(report.Expense.Total).IsEqualTo(195.66m);
        await Assert.That(report.Income.Total).IsEqualTo(37.94m);
        await Assert.That(report.Net).IsEqualTo(-157.72m);
    }

    [Test]
    public async Task Amounts_without_a_chf_rate_yet_are_left_out_of_the_totals_and_counted_as_pending()
    {
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var francs = await CreateAccountAsync(anna, "Cash", me);
        var euros = await CreateAccountAsync(anna, "Euro card", me, "EUR");
        var health = await CategoryIdAsync(anna, "Health");
        await AddAsync(anna, euros, -100m, "2026-11-18", health); // 92.66 CHF
        await AddAsync(anna, euros, -50m, "2026-11-02", health);  // no rate on or before the 2nd
        await AddAsync(anna, euros, 70m, "2026-11-03");           // no rate either
        await AddAsync(anna, francs, -10m, "2026-11-02", health); // CHF needs no rate

        var report = await EventuallyAsync(anna, "2026-11", r => r.Expense.Total > 10m);

        await Assert.That(report.Expense.Total).IsEqualTo(102.66m);
        await Assert.That(report.Expense.PendingConversion).IsEqualTo(1);
        await Assert.That(report.Expense.Categories.Single(c => c.Id == health).Total).IsEqualTo(102.66m);
        await Assert.That(report.Income.Total).IsEqualTo(0m);
        await Assert.That(report.Income.PendingConversion).IsEqualTo(1);
    }

    [Test]
    public async Task Months_can_be_switched_and_the_current_month_is_the_default()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash, -10m, "2026-11-18");
        await AddAsync(anna, cash, -20m, "2026-10-01");
        await AddAsync(anna, cash, -40m, "2026-02-28");

        var current = (await anna.GetFromJsonAsync<MonthlyReport>("/api/reports/monthly"))!; // clock: 2026-11-20

        await Assert.That(current.Month).IsEqualTo("2026-11");
        await Assert.That(current.Expense.Total).IsEqualTo(10m);
        await Assert.That((await MonthlyAsync(anna, "2026-10", "me")).Expense.Total).IsEqualTo(20m);
        await Assert.That((await MonthlyAsync(anna, "2026-02", "me")).Expense.Total).IsEqualTo(40m);
        await Assert.That((await MonthlyAsync(anna, "2025-11", "me")).Expense.Total).IsEqualTo(0m);
    }

    [Test]
    [Arguments("month=2026-13", "month")]
    [Arguments("month=November", "month")]
    [Arguments("owner=bob", "owner")]
    public async Task An_invalid_month_or_owner_is_rejected(string query, string field)
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.GetAsync($"/api/reports/monthly?{query}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await res.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors.Keys).Contains(field);
    }

    [Test]
    public async Task The_report_requires_login()
    {
        var res = await Client.GetAsync("/api/reports/monthly");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    /// Exchange rates are fetched in the background; polls the report until the condition holds.
    private static async Task<MonthlyReport> EventuallyAsync(HttpClient client, string month, Func<MonthlyReport, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var report = await MonthlyAsync(client, month, "me");
            if (done(report)) return report;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met: {report}");
            await Task.Delay(25);
        }
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name, Guid parentId)
    {
        var res = await client.PostAsJsonAsync("/api/categories", new { name, parentId });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create category failed: {(int)res.StatusCode}");
        return (await res.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private static async Task<MonthlyReport> MonthlyAsync(HttpClient client, string month, string? owner)
    {
        var res = await client.GetAsync($"/api/reports/monthly?month={month}" + (owner is null ? "" : $"&owner={owner}"));
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Report failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<MonthlyReport>())!;
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

    private static async Task<Guid> AddAsync(HttpClient client, Guid accountId, decimal amount, string date,
        Guid? categoryId = null, object? owner = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description = "Something", categoryId, owner });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!.Single(c => c.Path == path).Id;

    private record Me(Guid Id);

    private record Created(Guid Id);

    private record Category(Guid Id, string Path);

    private record MonthlyReport(string Month, string Owner, string Currency, Section Income, Section Expense, decimal Net);

    private record Section(decimal Total, int PendingConversion, decimal Uncategorised, decimal Contributions,
        List<CategoryTotal> Categories);

    private record CategoryTotal(Guid Id, string Name, Guid? ParentId, string Path, string? BuiltInKey,
        decimal Total, decimal Direct);
}
