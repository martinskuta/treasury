using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// Transfers and Contributions: pairs of Transactions on two of the Family's Accounts (GLOSSARY).
public class MatchingTests : ApiTestBase
{
    [Test]
    public async Task Imported_same_currency_pair_between_accounts_of_the_same_owner_becomes_a_transfer()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        await UploadOkAsync(anna, checking.Id, new TestStatement().Row("2026-11-16", -500m, "TO SAVINGS"));

        await UploadOkAsync(anna, savings.Id, new TestStatement().Row("2026-11-18", 500m, "FROM CHECKING"));

        var list = await ListAsync(anna);
        var outgoing = list.Single(t => t.Description == "TO SAVINGS");
        var incoming = list.Single(t => t.Description == "FROM CHECKING");
        await Assert.That(outgoing.Type).IsEqualTo("transfer");
        await Assert.That(incoming.Type).IsEqualTo("transfer");
        await Assert.That(outgoing.Link!.Kind).IsEqualTo("transfer");
        await Assert.That(outgoing.Link.TransactionId).IsEqualTo(incoming.Id);
        await Assert.That(incoming.Link!.TransactionId).IsEqualTo(outgoing.Id);
        await Assert.That(outgoing.Category).IsNull();
        await Assert.That(incoming.Category).IsNull();
        await Assert.That(await UncategorisedAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task A_currency_exchange_between_own_accounts_is_a_transfer_within_two_percent_and_one_day()
    {
        // 1 EUR = 0.966 CHF: +103.50 EUR is 99.98 CHF, against -100 CHF.
        App.ExchangeRates.Publish("2026-11-17", chf: 0.966m);
        App.ExchangeRates.Publish("2026-11-18", chf: 0.966m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var revolutChf = await CreateAccountAsync(anna, "Revolut CHF", me);
        var revolutEur = await CreateAccountAsync(anna, "Revolut EUR", me, currency: "EUR");
        await UploadOkAsync(anna, revolutEur.Id, new TestStatement().Row("2026-11-18", 103.50m, "EXCHANGED FROM CHF", currency: "EUR"));
        await RatesSyncedAsync(anna);

        await UploadOkAsync(anna, revolutChf.Id, new TestStatement().Row("2026-11-17", -100m, "EXCHANGED TO EUR"));

        var list = await ListAsync(anna);
        await Assert.That(list.Select(t => t.Type)).IsEquivalentTo(["transfer", "transfer"]);
        await Assert.That(list.Single(t => t.Currency == "CHF").Link!.TransactionId)
            .IsEqualTo(list.Single(t => t.Currency == "EUR").Id);
    }

    [Test]
    public async Task A_currency_exchange_off_by_more_than_two_percent_or_one_day_is_not_matched()
    {
        App.ExchangeRates.Publish("2026-11-16", chf: 0.966m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var revolutChf = await CreateAccountAsync(anna, "Revolut CHF", me);
        var revolutEur = await CreateAccountAsync(anna, "Revolut EUR", me, currency: "EUR");
        await UploadOkAsync(anna, revolutEur.Id, new TestStatement()
            .Row("2026-11-16", 108m, "OFF BY 4 PERCENT", currency: "EUR") // 104.33 CHF vs 100
            .Row("2026-11-19", 103.50m, "TWO DAYS LATER", currency: "EUR"));
        await RatesSyncedAsync(anna);

        await UploadOkAsync(anna, revolutChf.Id, new TestStatement().Row("2026-11-17", -100m, "EXCHANGED TO EUR"));

        await Assert.That((await ListAsync(anna)).Select(t => t.Type)).IsEquivalentTo(["income", "income", "expense"]);
    }

    [Test]
    public async Task A_payment_into_a_family_account_is_a_contribution_with_the_contribution_categories()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var personal = await CreateAccountAsync(anna, "Anna's account", await MeAsync(anna));
        var household = await CreateAccountAsync(anna, "Household", Family);
        await UploadOkAsync(anna, household.Id, new TestStatement().Row("2026-11-02", 1500m, "ANNA MONTHLY"));

        await UploadOkAsync(anna, personal.Id, new TestStatement().Row("2026-11-01", -1500m, "TO HOUSEHOLD"));

        var list = await ListAsync(anna);
        var sent = list.Single(t => t.Description == "TO HOUSEHOLD");
        var received = list.Single(t => t.Description == "ANNA MONTHLY");
        await Assert.That(sent.Type).IsEqualTo("expense");
        await Assert.That(received.Type).IsEqualTo("income");
        await Assert.That(sent.Link!.Kind).IsEqualTo("contribution");
        await Assert.That(received.Link!.TransactionId).IsEqualTo(sent.Id);
        await Assert.That(sent.Category!.Path).IsEqualTo("Contributions › To Family");
        await Assert.That(received.Category!.Path).IsEqualTo("Contributions › From Members");
        await Assert.That(await UncategorisedAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task Money_from_the_family_to_a_member_is_a_contribution_from_the_family()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var personal = await CreateAccountAsync(anna, "Anna's account", await MeAsync(anna));
        var household = await CreateAccountAsync(anna, "Household", Family);

        await AddAsync(anna, household.Id, -200m, "2026-11-10", "POCKET MONEY ANNA");
        await AddAsync(anna, personal.Id, 200m, "2026-11-10", "FROM HOUSEHOLD");

        var list = await ListAsync(anna);
        await Assert.That(list.Single(t => t.Amount < 0).Category!.Path).IsEqualTo("Contributions › To Members");
        await Assert.That(list.Single(t => t.Amount > 0).Category!.Path).IsEqualTo("Contributions › From Family");
    }

    [Test]
    public async Task With_more_than_one_candidate_nothing_is_matched()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var pillar3a = await CreateAccountAsync(anna, "Pillar 3a", me);
        await UploadOkAsync(anna, savings.Id, new TestStatement().Row("2026-11-17", 500m, "IN SAVINGS"));
        await UploadOkAsync(anna, pillar3a.Id, new TestStatement().Row("2026-11-18", 500m, "IN PILLAR"));

        await UploadOkAsync(anna, checking.Id, new TestStatement().Row("2026-11-17", -500m, "OUT"));

        await Assert.That((await ListAsync(anna)).Where(t => t.Type == "transfer")).IsEmpty();
    }

    [Test]
    public async Task Two_new_rows_competing_for_the_same_counterpart_are_left_unmatched()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        await UploadOkAsync(anna, savings.Id, new TestStatement().Row("2026-11-17", 500m, "IN SAVINGS"));

        await UploadOkAsync(anna, checking.Id, new TestStatement()
            .Row("2026-11-16", -500m, "OUT ONE").Row("2026-11-18", -500m, "OUT TWO"));

        await Assert.That((await ListAsync(anna)).Where(t => t.Type == "transfer")).IsEmpty();
    }

    [Test]
    public async Task Pairs_more_than_three_days_apart_with_other_amounts_or_in_the_same_account_are_not_matched()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        await UploadOkAsync(anna, savings.Id, new TestStatement()
            .Row("2026-11-14", 100m, "FOUR DAYS EARLIER")
            .Row("2026-11-18", 99.95m, "OTHER AMOUNT"));

        await UploadOkAsync(anna, checking.Id, new TestStatement()
            .Row("2026-11-18", -100m, "OUT").Row("2026-11-18", 100m, "SAME ACCOUNT REFUND"));

        await Assert.That((await ListAsync(anna)).Where(t => t.Type == "transfer")).IsEmpty();
    }

    [Test]
    public async Task A_manual_transaction_is_matched_when_added_and_when_its_amount_is_corrected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var cash = await CreateAccountAsync(anna, "Cash", me);
        await AddAsync(anna, checking.Id, -200m, "2026-11-18", "ATM");

        var typo = await AddAsync(anna, cash.Id, 20m, "2026-11-18", "Cash withdrawn");
        await Assert.That(typo.Type).IsEqualTo("income");
        var fixedAmount = await PatchOkAsync(anna, typo.Id, new { amount = 200m });

        await Assert.That(fixedAmount.Type).IsEqualTo("transfer");
        var cashIn = await AddAsync(anna, checking.Id, -50m, "2026-11-19", "ATM again");
        var matchedOnAdd = await AddAsync(anna, cash.Id, 50m, "2026-11-19", "Cash withdrawn again");
        await Assert.That(matchedOnAdd.Link!.TransactionId).IsEqualTo(cashIn.Id);
    }

    [Test]
    public async Task Correcting_a_matched_manual_transaction_so_it_no_longer_fits_breaks_the_pair()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var cash = await CreateAccountAsync(anna, "Cash", me);
        var atm = await AddAsync(anna, checking.Id, -200m, "2026-11-18", "ATM");
        var cashIn = await AddAsync(anna, cash.Id, 200m, "2026-11-18", "Cash withdrawn");

        var corrected = await PatchOkAsync(anna, cashIn.Id, new { amount = 180m });

        await Assert.That(corrected.Type).IsEqualTo("income");
        await Assert.That(corrected.Link).IsNull();
        await Assert.That((await GetAsync(anna, atm.Id)).Type).IsEqualTo("expense");
    }

    [Test]
    public async Task A_member_can_link_two_transactions_as_a_transfer_and_unlink_them()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        // A week apart: too far for automatic matching.
        var outgoing = await AddAsync(anna, checking.Id, -500m, "2026-11-01", "TO SAVINGS", categoryId: groceries);
        var incoming = await AddAsync(anna, savings.Id, 500m, "2026-11-08", "FROM CHECKING");

        var linkRes = await anna.PostAsJsonAsync($"/api/transactions/{outgoing.Id}/link", new { transactionId = incoming.Id });

        await Assert.That(linkRes.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var linked = (await linkRes.Content.ReadFromJsonAsync<Transaction>())!;
        await Assert.That(linked.Type).IsEqualTo("transfer");
        await Assert.That(linked.Category).IsNull();
        await Assert.That(linked.Link!.TransactionId).IsEqualTo(incoming.Id);
        await Assert.That((await GetAsync(anna, incoming.Id)).Link!.TransactionId).IsEqualTo(outgoing.Id);

        var unlinkRes = await anna.DeleteAsync($"/api/transactions/{incoming.Id}/link");

        await Assert.That(unlinkRes.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var unlinked = (await unlinkRes.Content.ReadFromJsonAsync<Transaction>())!;
        await Assert.That(unlinked.Type).IsEqualTo("income");
        await Assert.That(unlinked.Link).IsNull();
        await Assert.That((await GetAsync(anna, outgoing.Id)).Type).IsEqualTo("expense");
        await Assert.That((await UncategorisedAsync(anna)).Select(t => t.Id)).IsEquivalentTo([outgoing.Id, incoming.Id]);
    }

    [Test]
    public async Task Unlinking_a_contribution_takes_away_the_contribution_categories_matching_gave_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var personal = await CreateAccountAsync(anna, "Anna's account", await MeAsync(anna));
        var household = await CreateAccountAsync(anna, "Household", Family);
        var sent = await AddAsync(anna, personal.Id, -80m, "2026-11-10", "Dinner");
        var received = await AddAsync(anna, household.Id, 80m, "2026-11-10", "Refund");
        await Assert.That(received.Link!.Kind).IsEqualTo("contribution");

        var res = await anna.DeleteAsync($"/api/transactions/{sent.Id}/link");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await ListAsync(anna);
        await Assert.That(list.All(t => t.Link is null && t.Category is null)).IsTrue();
        await Assert.That(list.Select(t => t.Type)).IsEquivalentTo(["expense", "income"]);
    }

    [Test]
    public async Task Linking_needs_two_unlinked_transactions_the_member_can_see_on_different_accounts_with_opposite_signs()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var bobs = await CreateAccountAsync(bob, "Bob's account", await MeAsync(bob));
        var a = await AddAsync(anna, checking.Id, -10m, "2026-11-01", "A");
        var sameAccount = await AddAsync(anna, checking.Id, 10m, "2026-11-11", "Same account");
        var sameSign = await AddAsync(anna, savings.Id, -11m, "2026-11-11", "Same sign");
        var hidden = await AddAsync(bob, bobs.Id, 10m, "2026-11-11", "Bob's");
        var pairedOut = await AddAsync(anna, checking.Id, -30m, "2026-11-20", "Paired out");
        await AddAsync(anna, savings.Id, 30m, "2026-11-20", "Paired in");

        foreach (var other in new[] { sameAccount.Id, sameSign.Id, hidden.Id, pairedOut.Id, Guid.NewGuid() })
            await AssertValidationErrorAsync(
                await anna.PostAsJsonAsync($"/api/transactions/{a.Id}/link", new { transactionId = other }), "transactionId");
        await Assert.That((await bob.PostAsJsonAsync($"/api/transactions/{a.Id}/link", new { transactionId = hidden.Id })).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.DeleteAsync($"/api/transactions/{pairedOut.Id}/link")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_transfer_refuses_a_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var outgoing = await AddAsync(anna, checking.Id, -500m, "2026-11-18", "TO SAVINGS");
        await AddAsync(anna, savings.Id, 500m, "2026-11-18", "FROM CHECKING");

        var res = await anna.PatchAsJsonAsync($"/api/transactions/{outgoing.Id}",
            new { categoryId = await CategoryIdAsync(anna, "Food › Groceries") });

        await AssertValidationErrorAsync(res, "categoryId");
    }

    [Test]
    public async Task Deleting_a_manual_leg_makes_the_other_leg_income_or_expense_again()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        var outgoing = await AddAsync(anna, checking.Id, -500m, "2026-11-18", "TO SAVINGS");
        var incoming = await AddAsync(anna, savings.Id, 500m, "2026-11-18", "FROM CHECKING");

        await Assert.That((await anna.DeleteAsync($"/api/transactions/{incoming.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var left = await GetAsync(anna, outgoing.Id);
        await Assert.That(left.Type).IsEqualTo("expense");
        await Assert.That(left.Link).IsNull();
    }

    // ---- helpers ----

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<CategoryRef>>("/api/categories"))!.Single(c => c.Path == path).Id;

    /// Waits until the exchange-rate job has stored rates for the visible Transactions (their CHF value is known).
    private static async Task RatesSyncedAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await ListAsync(client)).Any(t => t.ChfAmount is null))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Exchange rates were not synced.");
            await Task.Delay(25);
        }
    }

    private static async Task<Transaction> PatchOkAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PatchAsJsonAsync($"/api/transactions/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Patch failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static readonly object Family = new { kind = "family" };

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner,
        string currency = "CHF")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey = "test", currency, openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<ImportResult> UploadOkAsync(HttpClient client, Guid accountId, TestStatement statement)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(statement.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", $"statement-{Guid.NewGuid():N}.txt");
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount, string date,
        string description, object? owner = null, Guid? categoryId = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description, owner, categoryId });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Transaction> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Transaction>($"/api/transactions/{id}"))!;

    private static async Task<List<Transaction>> UncategorisedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions/uncategorised"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record ImportResult(SourceFile SourceFile, int ImportedCount);

    private record SourceFile(Guid Id);

    private record CategoryRef(Guid Id, string Name, string Path);

    private record LinkDto(string Kind, Guid? TransactionId);

    private record Transaction(Guid Id, Guid AccountId, DateOnly Date, decimal Amount, string Currency,
        decimal? ChfAmount, string Type, string Description, CategoryRef? Category, LinkDto? Link);
}
