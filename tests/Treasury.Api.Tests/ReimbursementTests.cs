using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Reimbursements and Amounts owed (GLOSSARY, ADR 0011, #38): an Owner pays another back for Transactions made on
/// the other's Account. Anna's personal Account and the Family's "Household" Account, unless a test says otherwise.
public class ReimbursementTests : ApiTestBase
{
    [Test]
    public async Task Money_from_the_family_to_a_member_is_a_reimbursement_without_a_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);

        var paid = await AddAsync(anna, household.Id, -200m, "2026-11-30", "TO ANNA");
        var received = await AddAsync(anna, personal.Id, 200m, "2026-11-30", "FROM HOUSEHOLD");

        paid = await GetAsync(anna, paid.Id);
        await Assert.That(paid.Type).IsEqualTo("reimbursement");
        await Assert.That(received.Type).IsEqualTo("reimbursement");
        await Assert.That(received.Link!.Kind).IsEqualTo("reimbursement");
        await Assert.That(received.Link.TransactionId).IsEqualTo(paid.Id);
        await Assert.That(paid.Category).IsNull();
        await Assert.That(received.Category).IsNull();
        await Assert.That(await UncategorisedAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task Linking_by_hand_money_from_the_family_to_a_member_makes_a_reimbursement()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);
        // A week apart: too far for automatic matching.
        var paid = await AddAsync(anna, household.Id, -200m, "2026-11-01", "TO ANNA");
        var received = await AddAsync(anna, personal.Id, 200m, "2026-11-08", "FROM HOUSEHOLD");

        var res = await anna.PostAsJsonAsync($"/api/transactions/{paid.Id}/link", new { transactionId = received.Id });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await res.Content.ReadFromJsonAsync<Transaction>())!.Link!.Kind).IsEqualTo("reimbursement");
    }

    [Test]
    public async Task A_reimbursement_can_be_switched_to_a_contribution_and_back()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);
        var paid = await AddAsync(anna, household.Id, -200m, "2026-11-30", "POCKET MONEY");
        var received = await AddAsync(anna, personal.Id, 200m, "2026-11-30", "FROM HOUSEHOLD");

        var switched = await SwitchOkAsync(anna, received.Id, "contribution");

        await Assert.That(switched.Type).IsEqualTo("income");
        await Assert.That(switched.Link!.Kind).IsEqualTo("contribution");
        await Assert.That(switched.Category!.Path).IsEqualTo("Contributions › From Family");
        await Assert.That(switched.ManuallySet).Contains("linkKind");
        paid = await GetAsync(anna, paid.Id);
        await Assert.That(paid.Type).IsEqualTo("expense");
        await Assert.That(paid.Category!.Path).IsEqualTo("Contributions › To Members");

        var back = await SwitchOkAsync(anna, paid.Id, "reimbursement");

        await Assert.That(back.Type).IsEqualTo("reimbursement");
        await Assert.That(back.Category).IsNull();
        await Assert.That((await GetAsync(anna, received.Id)).Category).IsNull();
    }

    [Test]
    public async Task A_contribution_into_a_family_account_can_be_switched_to_a_reimbursement()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);
        var sent = await AddAsync(anna, personal.Id, -50m, "2026-11-20", "PAYING BACK THE FAMILY");
        var received = await AddAsync(anna, household.Id, 50m, "2026-11-20", "FROM ANNA");
        await Assert.That(received.Link!.Kind).IsEqualTo("contribution");

        var switched = await SwitchOkAsync(anna, sent.Id, "reimbursement");

        await Assert.That(switched.Type).IsEqualTo("reimbursement");
        await Assert.That((await GetAsync(anna, received.Id)).Type).IsEqualTo("reimbursement");
    }

    [Test]
    public async Task A_kind_chosen_by_hand_survives_owner_changes_until_both_sides_have_the_same_owner()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bob = new { kind = "member", memberId = await AddMemberAsync("bob") };
        var (personal, household) = await AccountsAsync(anna);
        await AddAsync(anna, household.Id, -200m, "2026-11-30", "POCKET MONEY");
        var received = await AddAsync(anna, personal.Id, 200m, "2026-11-30", "FROM HOUSEHOLD");
        await SwitchOkAsync(anna, received.Id, "contribution");

        // Family -> Bob would be a Reimbursement by default; the choice stays.
        var toBob = await PatchOkAsync(anna, received.Id, new { owner = bob });
        await Assert.That(toBob.Link!.Kind).IsEqualTo("contribution");

        var toFamily = await PatchOkAsync(anna, received.Id, new { owner = Family });
        await Assert.That(toFamily.Link!.Kind).IsEqualTo("transfer");

        // The choice was forgotten with the Transfer: back to the default.
        var toAnna = await PatchOkAsync(anna, received.Id, new { owner = await MeAsync(anna) });
        await Assert.That(toAnna.Link!.Kind).IsEqualTo("reimbursement");
        await Assert.That(toAnna.ManuallySet).DoesNotContain("linkKind");
    }

    [Test]
    public async Task Switching_needs_a_pair_with_different_owners_and_a_known_kind()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var (personal, household) = await AccountsAsync(anna);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var transferOut = await AddAsync(anna, personal.Id, -500m, "2026-11-18", "TO SAVINGS");
        await AddAsync(anna, savings.Id, 500m, "2026-11-18", "FROM CHECKING");
        var single = await AddAsync(anna, personal.Id, -9m, "2026-11-18", "Coffee");
        var paid = await AddAsync(anna, household.Id, -200m, "2026-11-30", "TO ANNA");
        await AddAsync(anna, personal.Id, 200m, "2026-11-30", "FROM HOUSEHOLD");

        await AssertValidationErrorAsync(await SwitchAsync(anna, transferOut.Id, "reimbursement"), "kind");
        await AssertValidationErrorAsync(await SwitchAsync(anna, single.Id, "reimbursement"), "kind");
        await AssertValidationErrorAsync(await SwitchAsync(anna, paid.Id, "transfer"), "kind");
        await AssertValidationErrorAsync(await SwitchAsync(anna, paid.Id, "gift"), "kind");
        await Assert.That((await SwitchAsync(anna, Guid.NewGuid(), "contribution")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_reimbursement_refuses_a_category_and_unlinking_makes_both_sides_income_and_expense()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);
        var paid = await AddAsync(anna, household.Id, -200m, "2026-11-30", "TO ANNA");
        var received = await AddAsync(anna, personal.Id, 200m, "2026-11-30", "FROM HOUSEHOLD");

        var res = await anna.PatchAsJsonAsync($"/api/transactions/{paid.Id}", new { categoryId = await CategoryIdAsync(anna, "Food › Groceries") });
        await AssertValidationErrorAsync(res, "categoryId");

        await Assert.That((await anna.DeleteAsync($"/api/transactions/{received.Id}/link")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ListAsync(anna)).Select(t => t.Type)).IsEquivalentTo(["expense", "income"]);
    }

    [Test]
    public async Task A_family_expense_paid_from_a_personal_account_counts_once_for_the_family_and_not_for_the_member()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        await AddAsync(anna, personal.Id, -120m, "2026-11-03", "MIGROS", owner: Family, categoryId: groceries);
        await AddAsync(anna, household.Id, -120m, "2026-11-12", "TO ANNA");
        await AddAsync(anna, personal.Id, 120m, "2026-11-12", "FROM HOUSEHOLD");

        var family = await MonthlyAsync(anna, "family");
        var mine = await MonthlyAsync(anna, "me");

        await Assert.That(family.Expense.Total).IsEqualTo(120m);
        await Assert.That(family.Income.Total).IsEqualTo(0m);
        await Assert.That(mine.Expense.Total).IsEqualTo(0m);
        await Assert.That(mine.Income.Total).IsEqualTo(0m);
    }

    [Test]
    public async Task One_reimbursement_pays_back_several_expenses_oldest_first_and_the_leftover_stays_owed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var (personal, household) = await AccountsAsync(anna);
        var migros = await AddAsync(anna, personal.Id, -120m, "2026-11-03", "MIGROS", owner: Family);
        var pharmacy = await AddAsync(anna, personal.Id, -80.35m, "2026-11-14", "PHARMACY", owner: Family);
        var own = await AddAsync(anna, personal.Id, -9m, "2026-11-15", "Coffee");

        var before = (await AmountsAsync(anna)).Amounts.Single();
        await Assert.That(before.Debtor.Kind).IsEqualTo("family");
        await Assert.That(before.Creditor.MemberId).IsEqualTo(annaId);
        await Assert.That(before.Amount).IsEqualTo(200.35m);
        await Assert.That(migros.PaidBack!.Status).IsEqualTo("outstanding");
        await Assert.That(migros.PaidBack.Outstanding).IsEqualTo(120m);
        await Assert.That(own.PaidBack).IsNull();

        await AddAsync(anna, household.Id, -200m, "2026-11-30", "TO ANNA");
        var received = await AddAsync(anna, personal.Id, 200m, "2026-11-30", "FROM HOUSEHOLD");

        var after = (await AmountsAsync(anna)).Amounts.Single();
        await Assert.That(after.Debtor.Kind).IsEqualTo("family");
        await Assert.That(after.Amount).IsEqualTo(0.35m);
        await Assert.That((await GetAsync(anna, migros.Id)).PaidBack!.Status).IsEqualTo("paidBack");
        var partly = (await GetAsync(anna, pharmacy.Id)).PaidBack!;
        await Assert.That(partly.Status).IsEqualTo("partlyPaidBack");
        await Assert.That(partly.Outstanding).IsEqualTo(0.35m);
        await Assert.That(received.PaidBack).IsNull();

        var open = (await anna.GetFromJsonAsync<List<Transaction>>("/api/transactions?notPaidBack=true"))!;
        await Assert.That(open.Select(t => t.Id)).IsEquivalentTo([pharmacy.Id]);
    }

    [Test]
    public async Task A_reimbursement_larger_than_the_expenses_leaves_the_member_owing_the_family()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var (personal, household) = await AccountsAsync(anna);
        await AddAsync(anna, personal.Id, -200.35m, "2026-11-03", "MIGROS", owner: Family);
        await AddAsync(anna, household.Id, -210m, "2026-11-30", "TO ANNA");
        await AddAsync(anna, personal.Id, 210m, "2026-11-30", "FROM HOUSEHOLD");

        var amount = (await AmountsAsync(anna)).Amounts.Single();
        await Assert.That(amount.Debtor.MemberId).IsEqualTo(annaId);
        await Assert.That(amount.Creditor.Kind).IsEqualTo("family");
        await Assert.That(amount.Amount).IsEqualTo(9.65m);

        // The next Family Expense uses the leftover up first.
        var next = await AddAsync(anna, personal.Id, -30m, "2026-12-02", "BAKERY", owner: Family);
        await Assert.That(next.PaidBack!.Status).IsEqualTo("partlyPaidBack");
        await Assert.That(next.PaidBack.Outstanding).IsEqualTo(20.35m);
        await Assert.That((await AmountsAsync(anna)).Amounts.Single().Debtor.Kind).IsEqualTo("family");
    }

    [Test]
    public async Task Family_income_on_a_personal_account_and_a_personal_expense_on_a_family_account_lower_what_the_family_owes()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var (personal, household) = await AccountsAsync(anna);
        await AddAsync(anna, personal.Id, -400m, "2026-11-03", "MIGROS", owner: Family);
        var refund = await AddAsync(anna, personal.Id, 30m, "2026-11-05", "MIGROS REFUND", owner: Family);
        var shoes = await AddAsync(anna, household.Id, -50m, "2026-11-08", "SHOES FOR ANNA", owner: me);

        var amount = (await AmountsAsync(anna)).Amounts.Single();

        await Assert.That(amount.Debtor.Kind).IsEqualTo("family");
        await Assert.That(amount.Amount).IsEqualTo(320m);
        await Assert.That(refund.PaidBack!.Status).IsEqualTo("paidBack");
        await Assert.That((await GetAsync(anna, shoes.Id)).PaidBack!.Status).IsEqualTo("paidBack");
    }

    [Test]
    public async Task An_expense_in_eur_counts_in_chf_and_waits_while_it_has_no_rate()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var eur = await CreateAccountAsync(anna, "Anna's EUR", me, currency: "EUR");
        var hotel = await AddAsync(anna, eur.Id, -100m, "2026-11-17", "HOTEL", owner: Family);

        var pending = (await AmountsAsync(anna)).Amounts.Single();
        await Assert.That(pending.Amount).IsEqualTo(0m);
        await Assert.That(pending.PendingConversion).IsEqualTo(1);
        await Assert.That(hotel.PaidBack).IsNull();

        App.ExchangeRates.Publish("2026-11-17", chf: 0.966m);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await GetAsync(anna, hotel.Id)).ChfAmount is null)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The rate was not synced.");
            await Task.Delay(25);
            App.Clock.Advance(TimeSpan.FromHours(1));
        }

        var amount = (await AmountsAsync(anna)).Amounts.Single();
        await Assert.That(amount.Amount).IsEqualTo(96.60m);
        await Assert.That(amount.PendingConversion).IsEqualTo(0);
    }

    [Test]
    public async Task Only_transactions_from_the_start_date_count_towards_the_amount_owed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, household) = await AccountsAsync(anna);
        var old = await AddAsync(anna, personal.Id, -70m, "2026-10-20", "OLD MIGROS", owner: Family);
        await AddAsync(anna, household.Id, -500m, "2026-10-28", "OLD PAYBACK");
        await AddAsync(anna, personal.Id, 500m, "2026-10-28", "OLD PAYBACK IN");
        await AddAsync(anna, personal.Id, -120m, "2026-11-03", "MIGROS", owner: Family);
        await Assert.That((await SettingsAsync(anna)).AmountOwedStartDate).IsNull();
        await Assert.That((await AmountsAsync(anna)).Amounts.Single().Debtor.Kind).IsEqualTo("member");

        var saved = await anna.PutAsJsonAsync("/api/settings", new { amountOwedStartDate = "2026-11-01" });

        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await SettingsAsync(anna)).AmountOwedStartDate).IsEqualTo(new DateOnly(2026, 11, 1));
        var amounts = await AmountsAsync(anna);
        await Assert.That(amounts.StartDate).IsEqualTo(new DateOnly(2026, 11, 1));
        await Assert.That(amounts.Amounts.Single().Debtor.Kind).IsEqualTo("family");
        await Assert.That(amounts.Amounts.Single().Amount).IsEqualTo(120m);
        await Assert.That((await GetAsync(anna, old.Id)).PaidBack).IsNull();

        await anna.PutAsJsonAsync("/api/settings", new { amountOwedStartDate = (string?)null });
        await Assert.That((await SettingsAsync(anna)).AmountOwedStartDate).IsNull();
        await Assert.That((await AmountsAsync(anna)).Amounts.Single().Amount).IsEqualTo(310m);
    }

    [Test]
    public async Task Changing_the_owner_back_takes_the_transaction_out_of_the_amount_owed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var (personal, _) = await AccountsAsync(anna);
        var migros = await AddAsync(anna, personal.Id, -120m, "2026-11-03", "MIGROS", owner: Family);
        await Assert.That((await AmountsAsync(anna)).Amounts.Count).IsEqualTo(1);

        var mine = await PatchOkAsync(anna, migros.Id, new { owner = await MeAsync(anna) });

        await Assert.That(mine.PaidBack).IsNull();
        await Assert.That((await AmountsAsync(anna)).Amounts).IsEmpty();
    }

    [Test]
    public async Task An_amount_owed_between_two_members_is_seen_only_by_those_two_and_the_familys_by_everyone()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        using var carol = await LoginAsNewMemberAsync("carol");
        var annaId = await MemberIdAsync(anna);
        var bobId = await MemberIdAsync(bob);
        var (personal, _) = await AccountsAsync(anna);
        await AddAsync(anna, personal.Id, -40m, "2026-11-03", "BOB'S BOOK", owner: new { kind = "member", memberId = bobId });
        await AddAsync(anna, personal.Id, -120m, "2026-11-04", "MIGROS", owner: Family);

        foreach (var client in new[] { anna, bob })
        {
            var amounts = (await AmountsAsync(client)).Amounts;
            await Assert.That(amounts.Count).IsEqualTo(2);
            await Assert.That(amounts[0].Debtor.Kind).IsEqualTo("family"); // pairs with the Family come first
            await Assert.That(amounts[1].Debtor.MemberId).IsEqualTo(bobId);
            await Assert.That(amounts[1].Creditor.MemberId).IsEqualTo(annaId);
            await Assert.That(amounts[1].Amount).IsEqualTo(40m);
        }
        var carols = (await AmountsAsync(carol)).Amounts.Single();
        await Assert.That(carols.Debtor.Kind).IsEqualTo("family");
        await Assert.That(carols.Amount).IsEqualTo(120m);
    }

    // ---- helpers ----

    private static readonly object Family = new { kind = "family" };

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private static async Task<object> MeAsync(HttpClient client) => new { kind = "member", memberId = await MemberIdAsync(client) };

    /// The Member's personal Account and the Family's Household Account.
    private static async Task<(Account Personal, Account Household)> AccountsAsync(HttpClient client) =>
        (await CreateAccountAsync(client, "Personal", await MeAsync(client)), await CreateAccountAsync(client, "Household", Family));

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner, string currency = "CHF")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey = "test", currency, openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount, string date,
        string description, object? owner = null, Guid? categoryId = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions", new { accountId, date, amount, description, owner, categoryId });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<Transaction> PatchOkAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PatchAsJsonAsync($"/api/transactions/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Patch failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient client, Guid id, string kind) =>
        client.PutAsJsonAsync($"/api/transactions/{id}/link/kind", new { kind });

    private static async Task<Transaction> SwitchOkAsync(HttpClient client, Guid id, string kind)
    {
        var res = await SwitchAsync(client, id, kind);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Switch failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Transaction> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Transaction>($"/api/transactions/{id}"))!;

    private static async Task<List<Transaction>> UncategorisedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions/uncategorised"))!;

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<CategoryRef>>("/api/categories"))!.Single(c => c.Path == path).Id;

    private static async Task<AmountsOwed> AmountsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AmountsOwed>("/api/amounts-owed"))!;

    private static async Task<Settings> SettingsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Settings>("/api/settings"))!;

    /// November 2026 as the Monthly overview counts it.
    private static async Task<MonthlyReport> MonthlyAsync(HttpClient client, string owner) =>
        (await client.GetFromJsonAsync<MonthlyReport>($"/api/reports/monthly?month=2026-11&owner={owner}"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record CategoryRef(Guid Id, string Name, string Path);

    private record LinkDto(string Kind, Guid? TransactionId);

    private record PaidBackDto(string Status, decimal Outstanding);

    private record Transaction(Guid Id, DateOnly Date, decimal Amount, decimal? ChfAmount, string Type,
        string Description, CategoryRef? Category, LinkDto? Link, PaidBackDto? PaidBack, string[] ManuallySet);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record AmountOwedDto(OwnerDto Debtor, OwnerDto Creditor, decimal Amount, int PendingConversion);

    private record AmountsOwed(string Currency, DateOnly? StartDate, List<AmountOwedDto> Amounts);

    private record Settings(DateOnly? AmountOwedStartDate);

    private record MonthlyReport(Section Income, Section Expense);

    private record Section(decimal Total);
}
