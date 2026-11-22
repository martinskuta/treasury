using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

public class TransactionTests : ApiTestBase
{
    [Test]
    public async Task A_member_can_add_a_manual_transaction_to_their_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));

        var res = await anna.PostAsJsonAsync("/api/transactions", new
        {
            accountId = wallet.Id,
            date = "2026-11-18",
            amount = -12.50m,
            description = "Bakery",
            note = "Croissants",
        });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var list = await ListAsync(anna);
        await Assert.That(list).Count().IsEqualTo(1);
        var t = list[0];
        await Assert.That(t.AccountId).IsEqualTo(wallet.Id);
        await Assert.That(t.Date).IsEqualTo(new DateOnly(2026, 11, 18));
        await Assert.That(t.Amount).IsEqualTo(-12.50m);
        await Assert.That(t.Currency).IsEqualTo("CHF");
        await Assert.That(t.Description).IsEqualTo("Bakery");
        await Assert.That(t.Note).IsEqualTo("Croissants");
        await Assert.That(t.Origin).IsEqualTo("manual");
    }

    [Test]
    public async Task Positive_amounts_are_income_and_negative_amounts_are_expense()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));

        var salary = await AddAsync(anna, wallet.Id, 5000m, description: "Salary");
        var rent = await AddAsync(anna, wallet.Id, -1800m, description: "Rent");

        await Assert.That(salary.Type).IsEqualTo("income");
        await Assert.That(rent.Type).IsEqualTo("expense");
        var list = await ListAsync(anna);
        await Assert.That(list.Single(t => t.Description == "Salary").Type).IsEqualTo("income");
        await Assert.That(list.Single(t => t.Description == "Rent").Type).IsEqualTo("expense");
    }

    [Test]
    [Arguments("amount")]
    [Arguments("description")]
    [Arguments("date")]
    public async Task A_transaction_needs_a_date_an_amount_and_a_description(string missing)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = wallet.Id, ["date"] = "2026-11-18", ["amount"] = -5m, ["description"] = "Coffee",
        };
        body[missing] = missing == "description" ? "  " : null;

        var res = await anna.PostAsJsonAsync("/api/transactions", body);

        await AssertValidationErrorAsync(res, missing);
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task A_zero_amount_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));

        var res = await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = wallet.Id, date = "2026-11-18", amount = 0m, description = "Nothing" });

        await AssertValidationErrorAsync(res, "amount");
    }

    [Test]
    public async Task The_account_balance_is_the_opening_balance_plus_transactions_from_the_opening_date_on()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna), openingBalance: 100m, openingDate: "2026-01-01");
        var other = await CreateAccountAsync(anna, "Other", await MeAsync(anna), openingBalance: 0m);

        await AddAsync(anna, wallet.Id, -50m, date: "2025-12-31");   // before the opening date: already in the opening balance
        await AddAsync(anna, wallet.Id, -7m, date: "2026-01-01");    // on the opening date: counts
        await AddAsync(anna, wallet.Id, 20m, date: "2026-01-02");
        await AddAsync(anna, wallet.Id, -5.25m, date: "2026-11-18");
        await AddAsync(anna, other.Id, -1000m, date: "2026-11-18");  // another Account

        await Assert.That((await GetAccountAsync(anna, wallet.Id)).Balance).IsEqualTo(107.75m);
        var accounts = (await anna.GetFromJsonAsync<List<Account>>("/api/accounts"))!;
        await Assert.That(accounts.Single(a => a.Id == wallet.Id).Balance).IsEqualTo(107.75m);
        await Assert.That(accounts.Single(a => a.Id == other.Id).Balance).IsEqualTo(-1000m);
    }

    [Test]
    public async Task A_transaction_is_owned_by_its_accounts_owner_by_default()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var joint = await CreateAccountAsync(anna, "Joint", Family);

        var personal = await AddAsync(anna, wallet.Id, -5m);
        var shared = await AddAsync(anna, joint.Id, -5m);

        await Assert.That(personal.Owner).IsEqualTo(new OwnerDto("member", annaId, "Anna (anna)"));
        await Assert.That(shared.Owner).IsEqualTo(new OwnerDto("family", null, "Family"));
    }

    [Test]
    public async Task Members_do_not_see_transactions_in_another_members_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annasWallet = await CreateAccountAsync(anna, "Anna cash", await MeAsync(anna));
        var secret = await AddAsync(anna, annasWallet.Id, -99m, description: "Secret");

        await Assert.That(await ListAsync(bob)).IsEmpty();
        await Assert.That((await bob.GetAsync($"/api/transactions/{secret.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var seenByAnna = await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{secret.Id}");
        await Assert.That(seenByAnna!.Description).IsEqualTo("Secret");
    }

    [Test]
    public async Task Members_cannot_add_transactions_to_another_members_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annasWallet = await CreateAccountAsync(anna, "Anna cash", await MeAsync(anna));

        var res = await bob.PostAsJsonAsync("/api/transactions",
            new { accountId = annasWallet.Id, date = "2026-11-18", amount = -1m, description = "Sneaky" });

        await AssertValidationErrorAsync(res, "accountId");
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task Every_member_sees_the_transactions_of_a_family_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        await AddAsync(anna, joint.Id, -40m, description: "Groceries");
        // Even when Bob takes one over, it stays in the Family Account that Anna sees in full.
        await AddAsync(bob, joint.Id, -15m, description: "Bob's lunch", owner: await MeAsync(bob));

        await Assert.That((await ListAsync(anna)).Select(t => t.Description)).IsEquivalentTo(["Groceries", "Bob's lunch"]);
        await Assert.That((await ListAsync(bob)).Select(t => t.Description)).IsEquivalentTo(["Groceries", "Bob's lunch"]);
        await Assert.That((await GetAccountAsync(anna, joint.Id)).Balance).IsEqualTo(-55m);
    }

    [Test]
    public async Task A_family_owned_transaction_in_a_personal_account_is_visible_to_every_member_but_the_rest_of_the_account_is_not()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annasCard = await CreateAccountAsync(anna, "Anna card", await MeAsync(anna));
        await AddAsync(anna, annasCard.Id, -30m, description: "Anna's book");
        var dinner = await AddAsync(anna, annasCard.Id, -120m, description: "Family dinner", owner: Family);

        var seenByBob = await ListAsync(bob);
        await Assert.That(seenByBob.Select(t => t.Id)).IsEquivalentTo([dinner.Id]);
        await Assert.That(seenByBob[0].Owner.Kind).IsEqualTo("family");
        await Assert.That((await bob.GetAsync($"/api/accounts/{annasCard.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await ListAsync(anna)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_transaction_reassigned_to_bob_from_alices_account_is_visible_to_bob_without_the_rest_of_the_account()
    {
        using var alice = await LoginAsNewMemberAsync("alice");
        using var bob = await LoginAsNewMemberAsync("bob");
        using var carol = await LoginAsNewMemberAsync("carol");
        var bobId = await MemberIdAsync(bob);
        var alicesCard = await CreateAccountAsync(alice, "Alice card", await MeAsync(alice), openingBalance: 500m);
        await AddAsync(alice, alicesCard.Id, -30m, description: "Alice's book");
        var bobsTicket = await AddAsync(alice, alicesCard.Id, -80m, description: "Concert ticket for Bob");

        var res = await alice.PatchAsJsonAsync($"/api/transactions/{bobsTicket.Id}",
            new { owner = new { kind = "member", memberId = bobId } });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var seenByBob = await ListAsync(bob);
        await Assert.That(seenByBob.Select(t => t.Description)).IsEquivalentTo(["Concert ticket for Bob"]);
        await Assert.That(seenByBob[0].Owner).IsEqualTo(new OwnerDto("member", bobId, "Bob (bob)"));
        await Assert.That((await bob.GetAsync($"/api/accounts/{alicesCard.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetFromJsonAsync<List<Account>>("/api/accounts"))!).IsEmpty();
        // Alice still sees it (she owns the Account), and her balance still counts it.
        await Assert.That((await ListAsync(alice)).Select(t => t.Description))
            .IsEquivalentTo(["Alice's book", "Concert ticket for Bob"]);
        await Assert.That((await GetAccountAsync(alice, alicesCard.Id)).Balance).IsEqualTo(390m);
        // Nobody else does.
        await Assert.That(await ListAsync(carol)).IsEmpty();
    }

    [Test]
    public async Task A_transactions_owner_can_be_changed_to_the_family_or_any_member_and_back()
    {
        using var alice = await LoginAsNewMemberAsync("alice");
        using var bob = await LoginAsNewMemberAsync("bob");
        using var carol = await LoginAsNewMemberAsync("carol");
        var aliceId = await MemberIdAsync(alice);
        var card = await CreateAccountAsync(alice, "Alice card", await MeAsync(alice));
        var t = await AddAsync(alice, card.Id, -60m);

        await PatchOkAsync(alice, t.Id, new { owner = Family });
        await Assert.That((await ListAsync(carol)).Select(x => x.Id)).IsEquivalentTo([t.Id]);
        await Assert.That((await ListAsync(bob)).Select(x => x.Id)).IsEquivalentTo([t.Id]);

        await PatchOkAsync(alice, t.Id, new { owner = new { kind = "member", memberId = await MemberIdAsync(carol) } });
        await Assert.That((await ListAsync(carol)).Select(x => x.Id)).IsEquivalentTo([t.Id]);
        await Assert.That(await ListAsync(bob)).IsEmpty();

        var back = await PatchOkAsync(alice, t.Id, new { owner = new { kind = "member", memberId = aliceId } });
        await Assert.That(back.Owner).IsEqualTo(new OwnerDto("member", aliceId, "Alice (alice)"));
        await Assert.That(await ListAsync(carol)).IsEmpty();
        await Assert.That(await ListAsync(bob)).IsEmpty();
    }

    [Test]
    public async Task The_owner_of_a_reassigned_transaction_can_hand_it_back()
    {
        using var alice = await LoginAsNewMemberAsync("alice");
        using var bob = await LoginAsNewMemberAsync("bob");
        var aliceId = await MemberIdAsync(alice);
        var card = await CreateAccountAsync(alice, "Alice card", await MeAsync(alice));
        var t = await AddAsync(alice, card.Id, -60m, owner: await MeAsync(bob));

        await PatchOkAsync(bob, t.Id, new { owner = new { kind = "member", memberId = aliceId } });

        await Assert.That(await ListAsync(bob)).IsEmpty();
        await Assert.That((await ListAsync(alice)).Single().Owner.MemberId).IsEqualTo(aliceId);
    }

    [Test]
    [Arguments("unknown-member")]
    [Arguments("bad-kind")]
    public async Task An_invalid_owner_is_rejected(string variant)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -5m);
        object owner = variant == "bad-kind"
            ? new { kind = "someone" }
            : new { kind = "member", memberId = Guid.NewGuid() };

        var created = await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = wallet.Id, date = "2026-11-18", amount = -1m, description = "x", owner });
        var patched = await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { owner });

        await AssertValidationErrorAsync(created, "owner");
        await AssertValidationErrorAsync(patched, "owner");
    }

    [Test]
    public async Task A_member_can_edit_a_manual_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna), openingBalance: 100m);
        var t = await AddAsync(anna, wallet.Id, -10m, date: "2026-11-18", description: "Coffe");

        var edited = await PatchOkAsync(anna, t.Id,
            new { date = "2026-11-17", amount = 25m, description = "Refund", note = "From Migros" });

        await Assert.That(edited.Date).IsEqualTo(new DateOnly(2026, 11, 17));
        await Assert.That(edited.Amount).IsEqualTo(25m);
        await Assert.That(edited.Type).IsEqualTo("income");
        await Assert.That(edited.Description).IsEqualTo("Refund");
        await Assert.That(edited.Note).IsEqualTo("From Migros");
        var reloaded = await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{t.Id}");
        await Assert.That(reloaded).IsEqualTo(edited);
        await Assert.That((await GetAccountAsync(anna, wallet.Id)).Balance).IsEqualTo(125m);
    }

    [Test]
    public async Task Editing_only_the_note_keeps_everything_else_and_an_empty_note_clears_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -10m, description: "Coffee");

        var withNote = await PatchOkAsync(anna, t.Id, new { note = "With Bob" });
        var cleared = await PatchOkAsync(anna, t.Id, new { note = "" });

        await Assert.That(withNote with { Note = null }).IsEqualTo(t);
        await Assert.That(cleared).IsEqualTo(t);
    }

    [Test]
    public async Task Invalid_edits_are_rejected_and_change_nothing()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -10m, description: "Coffee");

        await AssertValidationErrorAsync(
            await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { amount = 0m, note = "x" }), "amount");
        await AssertValidationErrorAsync(
            await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { description = " " }), "description");

        await Assert.That(await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{t.Id}")).IsEqualTo(t);
    }

    [Test]
    public async Task A_member_can_delete_a_manual_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna), openingBalance: 100m);
        var keep = await AddAsync(anna, wallet.Id, -10m);
        var gone = await AddAsync(anna, wallet.Id, -30m);

        var res = await anna.DeleteAsync($"/api/transactions/{gone.Id}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await ListAsync(anna)).Select(t => t.Id)).IsEquivalentTo([keep.Id]);
        await Assert.That((await anna.GetAsync($"/api/transactions/{gone.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await GetAccountAsync(anna, wallet.Id)).Balance).IsEqualTo(90m);
    }

    [Test]
    public async Task Members_who_cannot_see_a_transaction_can_neither_edit_nor_delete_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -10m);

        var patched = await bob.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { owner = await MeAsync(bob) });
        var deleted = await bob.DeleteAsync($"/api/transactions/{t.Id}");

        await Assert.That(patched.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{t.Id}")).IsEqualTo(t);
    }

    [Test]
    public async Task The_owner_of_a_transaction_in_someone_elses_account_cannot_change_its_facts_or_delete_it()
    {
        using var alice = await LoginAsNewMemberAsync("alice");
        using var bob = await LoginAsNewMemberAsync("bob");
        var card = await CreateAccountAsync(alice, "Alice card", await MeAsync(alice));
        var t = await AddAsync(alice, card.Id, -80m, owner: await MeAsync(bob));

        var patched = await bob.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { amount = -1m });
        var deleted = await bob.DeleteAsync($"/api/transactions/{t.Id}");

        await Assert.That(patched.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await alice.GetFromJsonAsync<Transaction>($"/api/transactions/{t.Id}")).IsEqualTo(t);
        // ...but can add a note to it, like any classification.
        await Assert.That((await PatchOkAsync(bob, t.Id, new { note = "My ticket" })).Note).IsEqualTo("My ticket");
    }

    [Test]
    public async Task Any_member_can_edit_and_delete_manual_transactions_in_a_family_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var t = await AddAsync(anna, joint.Id, -10m);

        await PatchOkAsync(bob, t.Id, new { amount = -12m });
        var deleted = await bob.DeleteAsync($"/api/transactions/{t.Id}");

        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task The_owners_a_transaction_can_be_given_are_the_family_and_every_member()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var owners = (await anna.GetFromJsonAsync<List<OwnerDto>>("/api/owners"))!;

        await Assert.That(owners.Select(o => o.Name)).IsEquivalentTo(["Family", "admin (admin)", "Anna (anna)", "Bob (bob)"]);
        await Assert.That(owners[0]).IsEqualTo(new OwnerDto("family", null, "Family"));
        await Assert.That(owners.Single(o => o.Name == "Bob (bob)").MemberId).IsEqualTo(await MemberIdAsync(bob));
    }

    [Test]
    public async Task Deactivated_members_are_not_offered_as_owners()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bobId = await AddMemberAsync("bob");
        await DeactivateAsync(bobId);

        var owners = (await anna.GetFromJsonAsync<List<OwnerDto>>("/api/owners"))!;

        await Assert.That(owners.Select(o => o.Name)).IsEquivalentTo(["Family", "admin (admin)", "Anna (anna)"]);
    }

    [Test]
    public async Task A_deactivated_member_cannot_be_chosen_as_a_transactions_owner()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bobId = await AddMemberAsync("bob");
        await DeactivateAsync(bobId);
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -5m);
        var bob = new { kind = "member", memberId = bobId };

        var created = await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = wallet.Id, date = "2026-11-18", amount = -7m, description = "Coffee", owner = bob });
        var patched = await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { owner = bob });

        await AssertValidationErrorAsync(created, "owner");
        await AssertValidationErrorAsync(patched, "owner");
        var list = await ListAsync(anna);
        await Assert.That(list.Select(x => x.Id)).IsEquivalentTo([t.Id]);
        await Assert.That(list[0].Owner.MemberId).IsEqualTo(await MemberIdAsync(anna));
    }

    [Test]
    public async Task An_admin_cannot_see_another_members_personal_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var card = await CreateAccountAsync(anna, "Anna card", await MeAsync(anna));
        var t = await AddAsync(anna, card.Id, -30m, description: "Anna's book");
        await LoginAsAdminAsync();

        await Assert.That(await ListAsync(Client)).IsEmpty();
        await Assert.That((await Client.GetAsync($"/api/transactions/{t.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await Client.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { note = "peek" })).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Transactions_require_login()
    {
        await Assert.That((await Client.GetAsync("/api/transactions")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private static async Task<Transaction> PatchOkAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PatchAsJsonAsync($"/api/transactions/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Patch failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static readonly object Family = new { kind = "family" };

    /// Deactivates a Member as the Admin (in a separate session), keeping their Accounts with them.
    private async Task DeactivateAsync(Guid memberId)
    {
        using var admin = NewClient();
        await LoginAsync(admin, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
        var res = await admin.PostAsJsonAsync($"/api/admin/members/{memberId}/deactivate",
            new { handover = Array.Empty<object>() });
        if (res.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Deactivation failed: {(int)res.StatusCode}");
    }

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = await MemberIdAsync(client) };

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner,
        decimal openingBalance = 0m, string openingDate = "2026-01-01")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name,
            owner,
            integrationKey = "test",
            currency = "CHF",
            openingBalance,
            openingDate,
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount,
        string date = "2026-11-18", string description = "Something", object? owner = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions", new { accountId, date, amount, description, owner });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id, string UserName);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Account(Guid Id, string Name, OwnerDto Owner, decimal OpeningBalance, DateOnly OpeningDate, decimal Balance);

    private record Transaction(Guid Id, Guid? AccountId, string? AccountName, DateOnly Date, decimal Amount, string Currency,
        string Type, string Description, string? Note, OwnerDto Owner, string Origin);
}
