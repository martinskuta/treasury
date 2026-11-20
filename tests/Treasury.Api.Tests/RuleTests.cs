using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Rules: shared conditions that categorise, tag and assign Transactions on import and on demand.
public class RuleTests : ApiTestBase
{
    [Test]
    public async Task An_imported_transaction_matching_a_rule_gets_its_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var card = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros", categoryId = groceries });

        await UploadOkAsync(anna, card.Id, new TestStatement()
            .Row("2026-11-10", -42.30m, "MIGROS ZUERICH HB")
            .Row("2026-11-11", -12.00m, "Coop Pronto"));

        var list = await ListAsync(anna);
        await Assert.That(list.Single(t => t.Description == "MIGROS ZUERICH HB").Category?.Id).IsEqualTo(groceries);
        await Assert.That(list.Single(t => t.Description == "Coop Pronto").Category).IsNull();
        await Assert.That(list.Single(t => t.Description == "MIGROS ZUERICH HB").ManuallySet).DoesNotContain("category");
    }

    [Test]
    public async Task Members_can_create_edit_reorder_and_delete_rules()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var transport = await CategoryIdAsync(anna, "Transport");
        var holiday = await CreateTagAsync(anna, "holiday");

        var migros = await CreateRuleAsync(anna, new { field = "description", match = "contains", text = " Migros ", categoryId = groceries });
        var sbb = await CreateRuleAsync(anna, new { field = "counterparty", match = "equals", text = "SBB", categoryId = transport, tagIds = new[] { holiday.Id } });
        var coop = await CreateRuleAsync(bob, new { field = "description", match = "contains", text = "coop", owner = Family });

        await Assert.That(migros.Text).IsEqualTo("Migros");
        await Assert.That(migros.Author.Name).IsEqualTo("anna");
        await Assert.That(migros.Category?.Path).IsEqualTo("Food › Groceries");
        await Assert.That(sbb.Tags.Select(t => t.Name)).IsEquivalentTo(["holiday"]);
        await Assert.That(coop.Owner!.Kind).IsEqualTo("family");
        await Assert.That(string.Join("|", (await RulesAsync(bob)).Select(r => r.Text))).IsEqualTo(string.Join("|", new[] { "Migros", "SBB", "coop" }));

        // Bob edits Anna's Rule: Rules are shared, and saving makes him the author.
        var edited = await PutRuleOkAsync(bob, sbb.Id, new { field = "counterparty", match = "contains", text = "SBB CFF", categoryId = transport });
        await Assert.That(edited.Text).IsEqualTo("SBB CFF");
        await Assert.That(edited.Match).IsEqualTo("contains");
        await Assert.That(edited.Tags).IsEmpty();
        await Assert.That(edited.Author.Name).IsEqualTo("bob");
        await Assert.That(edited.Priority).IsEqualTo(1);

        var reordered = await bob.PutAsJsonAsync("/api/rules/order", new { ruleIds = new[] { coop.Id, migros.Id, sbb.Id } });
        await Assert.That(reordered.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(string.Join("|", (await RulesAsync(anna)).Select(r => r.Text))).IsEqualTo(string.Join("|", new[] { "coop", "Migros", "SBB CFF" }));

        await Assert.That((await anna.DeleteAsync($"/api/rules/{migros.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var left = await RulesAsync(anna);
        await Assert.That(string.Join("|", left.Select(r => r.Text))).IsEqualTo(string.Join("|", new[] { "coop", "SBB CFF" }));
        await Assert.That(string.Join("|", left.Select(r => r.Priority))).IsEqualTo("0|1");
        await Assert.That((await anna.DeleteAsync($"/api/rules/{migros.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Invalid_rules_are_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var gone = await AddMemberAsync("gone");
        await DeactivateAsync(gone);

        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "amount", match = "contains", text = "x", categoryId = groceries }), "field");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "description", match = "regex", text = "x", categoryId = groceries }), "match");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "description", match = "contains", text = "  ", categoryId = groceries }), "text");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "description", match = "contains", text = "x" }), "actions");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "description", match = "contains", text = "x", categoryId = Guid.NewGuid() }), "categoryId");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "description", match = "contains", text = "x", tagIds = new[] { Guid.NewGuid() } }), "tagIds");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/rules", new { field = "description", match = "contains", text = "x", owner = new { kind = "member", memberId = gone } }), "owner");
        var rule = await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "x", categoryId = groceries });
        await AssertValidationErrorAsync(await anna.PutAsJsonAsync("/api/rules/order", new { ruleIds = Array.Empty<Guid>() }), "ruleIds");
        await AssertValidationErrorAsync(await anna.PutAsJsonAsync("/api/rules/order", new { ruleIds = new[] { rule.Id, rule.Id } }), "ruleIds");
        await Assert.That((await anna.PutAsJsonAsync($"/api/rules/{Guid.NewGuid()}", new { field = "description", match = "contains", text = "x", categoryId = groceries })).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task The_first_matching_rule_sets_category_and_owner_and_tags_of_all_matches_are_combined()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bobId = await AddMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var eatingOut = await CategoryIdAsync(anna, "Food › Eating out");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var lunch = await CreateTagAsync(anna, "lunch");
        var work = await CreateTagAsync(anna, "work");
        var holiday = await CreateTagAsync(anna, "holiday");
        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros restaurant", categoryId = eatingOut, tagIds = new[] { lunch.Id } });
        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros", categoryId = groceries, owner = new { kind = "member", memberId = bobId }, tagIds = new[] { work.Id } });
        await CreateRuleAsync(anna, new { field = "counterparty", match = "equals", text = "migros genossenschaft", owner = Family, tagIds = new[] { holiday.Id, lunch.Id } });

        await UploadOkAsync(anna, joint.Id, new TestStatement()
            .Row("2026-11-10", -18.50m, "MIGROS RESTAURANT ZH", counterparty: "Migros Genossenschaft")
            .Row("2026-11-11", -60.00m, "Migros Oerlikon")
            .Row("2026-11-12", -5.00m, "Kiosk", counterparty: "Migros Genossenschaft AG"));

        var list = await ListAsync(anna);
        var restaurant = list.Single(t => t.Description == "MIGROS RESTAURANT ZH");
        await Assert.That(restaurant.Category?.Id).IsEqualTo(eatingOut);
        await Assert.That(restaurant.Owner.MemberId).IsEqualTo(bobId);
        await Assert.That(restaurant.Tags.Select(t => t.Name)).IsEquivalentTo(["holiday", "lunch", "work"]);
        await Assert.That(restaurant.Tags.SelectMany(t => t.Sources).Distinct()).IsEquivalentTo(["rule"]);
        var shop = list.Single(t => t.Description == "Migros Oerlikon");
        await Assert.That(shop.Category?.Id).IsEqualTo(groceries);
        await Assert.That(shop.Owner.MemberId).IsEqualTo(bobId);
        await Assert.That(shop.Tags.Select(t => t.Name)).IsEquivalentTo(["work"]);
        var kiosk = list.Single(t => t.Description == "Kiosk"); // "equals" doesn't match a longer counterparty
        await Assert.That(kiosk.Category).IsNull();
        await Assert.That(kiosk.Tags).IsEmpty();
    }

    [Test]
    public async Task Rules_never_overwrite_a_field_set_by_hand()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bobId = await AddMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var health = await CategoryIdAsync(anna, "Health");
        var work = await CreateTagAsync(anna, "work");
        await UploadOkAsync(anna, joint.Id, new TestStatement()
            .Row("2026-11-10", -10m, "Migros A")
            .Row("2026-11-11", -20m, "Migros B")
            .Row("2026-11-12", -30m, "Migros C"));
        var before = await ListAsync(anna);
        var a = before.Single(t => t.Description == "Migros A");
        var b = before.Single(t => t.Description == "Migros B");
        var c = before.Single(t => t.Description == "Migros C");
        await PatchOkAsync(anna, a.Id, new { categoryId = health });
        await PatchOkAsync(anna, b.Id, new { owner = Family });
        await PatchOkAsync(anna, c.Id, new { addTagIds = new[] { work.Id } });
        await PatchOkAsync(anna, c.Id, new { removeTagIds = new[] { work.Id } });

        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros", categoryId = groceries, owner = new { kind = "member", memberId = bobId }, tagIds = new[] { work.Id } });
        await UploadOkAsync(anna, joint.Id, new TestStatement().Row("2026-11-13", -40m, "Migros D"));
        await ApplyAsync(anna);

        var after = await ListAsync(anna);
        var aAfter = after.Single(t => t.Id == a.Id);
        await Assert.That(aAfter.Category?.Id).IsEqualTo(health); // manual Category kept (not even in the queue)
        var bAfter = after.Single(t => t.Id == b.Id);
        await Assert.That(bAfter.Owner.Kind).IsEqualTo("family"); // manual Owner kept...
        await Assert.That(bAfter.Category?.Id).IsEqualTo(groceries); // ...while the Rule's other actions still apply
        var cAfter = after.Single(t => t.Id == c.Id);
        await Assert.That(cAfter.Tags).IsEmpty(); // removed by hand: sticks
        await Assert.That(cAfter.Owner.MemberId).IsEqualTo(bobId);
        var d = after.Single(t => t.Description == "Migros D");
        await Assert.That(d.Category?.Id).IsEqualTo(groceries);
        await Assert.That(d.Owner.MemberId).IsEqualTo(bobId);
        await Assert.That(d.Tags.Select(t => t.Name)).IsEquivalentTo(["work"]);
    }

    [Test]
    public async Task A_rule_only_applies_to_transactions_in_accounts_its_author_can_see()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annasCard = await CreateAccountAsync(anna, "Anna's card", await MeAsync(anna));
        var bobsCard = await CreateAccountAsync(bob, "Bob's card", await MeAsync(bob));
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        await CreateRuleAsync(bob, new { field = "description", match = "contains", text = "migros", categoryId = groceries });

        await UploadOkAsync(anna, annasCard.Id, new TestStatement().Row("2026-11-10", -10m, "Migros"));
        await UploadOkAsync(bob, bobsCard.Id, new TestStatement().Row("2026-11-10", -20m, "Migros"));
        await UploadOkAsync(anna, joint.Id, new TestStatement().Row("2026-11-10", -30m, "Migros"));

        await Assert.That((await ListAsync(anna)).Single(t => t.Amount == -10m).Category).IsNull();
        await Assert.That((await ListAsync(bob)).Single(t => t.Amount == -20m).Category?.Id).IsEqualTo(groceries);
        await Assert.That((await ListAsync(anna)).Single(t => t.Amount == -30m).Category?.Id).IsEqualTo(groceries);

        // Re-applying on demand doesn't widen the reach, whoever asks.
        await ApplyAsync(anna);
        await Assert.That((await ListAsync(anna)).Single(t => t.Amount == -10m).Category).IsNull();
    }

    [Test]
    public async Task Rules_of_a_deactivated_author_stop_applying_until_an_active_member_saves_them()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var rule = await CreateRuleAsync(bob, new { field = "description", match = "contains", text = "migros", categoryId = groceries });
        await DeactivateAsync(await MemberIdAsync(bob));

        await Assert.That((await RulesAsync(anna)).Single().Active).IsFalse();
        await UploadOkAsync(anna, joint.Id, new TestStatement().Row("2026-11-10", -10m, "Migros"));
        await Assert.That((await ListAsync(anna)).Single().Category).IsNull();

        var saved = await PutRuleOkAsync(anna, rule.Id, new { field = "description", match = "contains", text = "migros", categoryId = groceries });
        await Assert.That(saved.Active).IsTrue();
        await ApplyAsync(anna);
        await Assert.That((await ListAsync(anna)).Single().Category?.Id).IsEqualTo(groceries);
    }

    [Test]
    public async Task Rules_can_be_reapplied_on_demand_to_uncategorised_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var health = await CategoryIdAsync(anna, "Health");
        var old = await AddAsync(anna, cash.Id, -10m, "Migros old");
        var categorised = await AddAsync(anna, cash.Id, -20m, "Migros categorised");
        await PatchOkAsync(anna, categorised.Id, new { categoryId = health });
        var uncategorisedAgain = await AddAsync(anna, cash.Id, -25m, "Migros back in the queue");
        await PatchOkAsync(anna, uncategorisedAgain.Id, new { categoryId = health });
        await PatchOkAsync(anna, uncategorisedAgain.Id, new { uncategorise = true }); // "let Rules decide again"
        var other = await AddAsync(anna, cash.Id, -30m, "Coop");

        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros", categoryId = groceries });
        await Assert.That((await GetAsync(anna, old.Id)).Category).IsNull(); // creating a Rule doesn't run it

        var result = await ApplyAsync(anna);

        await Assert.That(result.Categorised).IsEqualTo(2);
        await Assert.That((await GetAsync(anna, old.Id)).Category?.Id).IsEqualTo(groceries);
        await Assert.That((await GetAsync(anna, uncategorisedAgain.Id)).Category?.Id).IsEqualTo(groceries);
        await Assert.That((await GetAsync(anna, categorised.Id)).Category?.Id).IsEqualTo(health);
        await Assert.That((await GetAsync(anna, other.Id)).Category).IsNull();
        var queue = await anna.GetFromJsonAsync<List<Transaction>>("/api/transactions/uncategorised");
        await Assert.That(queue!.Select(t => t.Id)).IsEquivalentTo([other.Id]);
    }

    [Test]
    public async Task Rules_never_categorise_transfers_or_contributions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var checking = await CreateAccountAsync(anna, "Checking", await MeAsync(anna));
        var savings = await CreateAccountAsync(anna, "Savings", await MeAsync(anna));
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var moves = await CreateTagAsync(anna, "moves");
        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "transfer", categoryId = groceries, tagIds = new[] { moves.Id } });

        await UploadOkAsync(anna, checking.Id, new TestStatement().Row("2026-11-10", -500m, "Transfer to savings"));
        await UploadOkAsync(anna, savings.Id, new TestStatement().Row("2026-11-10", 500m, "Transfer from checking"));
        await UploadOkAsync(anna, checking.Id, new TestStatement().Row("2026-11-12", -300m, "Transfer to joint"));
        await UploadOkAsync(anna, joint.Id, new TestStatement().Row("2026-11-12", 300m, "Transfer from Anna"));
        await ApplyAsync(anna);

        var list = await ListAsync(anna);
        var transferLeg = list.Single(t => t.Description == "Transfer from checking");
        await Assert.That(transferLeg.Type).IsEqualTo("transfer");
        await Assert.That(transferLeg.Category).IsNull();
        await Assert.That(transferLeg.Tags.Select(t => t.Name)).IsEquivalentTo(["moves"]); // Tags are fine on Transfers
        var contribution = list.Single(t => t.Description == "Transfer from Anna");
        await Assert.That(contribution.Category!.Path).StartsWith("Contributions");
    }

    [Test]
    public async Task An_owner_set_by_a_rule_updates_date_range_tags()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bobId = await AddMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        await CreateTagAsync(anna, "bob's trip", "2026-11-01", "2026-11-30", onlyMember: bobId);
        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "hotel", owner = new { kind = "member", memberId = bobId } });

        await UploadOkAsync(anna, joint.Id, new TestStatement().Row("2026-11-10", -200m, "Hotel Lugano").Row("2026-11-10", -50m, "Bakery"));

        var list = await ListAsync(anna);
        var hotel = list.Single(t => t.Description == "Hotel Lugano");
        await Assert.That(hotel.Owner.MemberId).IsEqualTo(bobId);
        await Assert.That(hotel.Tags.Select(t => t.Name)).IsEquivalentTo(["bob's trip"]);
        await Assert.That(list.Single(t => t.Description == "Bakery").Tags).IsEmpty();
    }

    [Test]
    public async Task A_rule_does_not_assign_an_owner_who_has_been_deactivated()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var bobId = await AddMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros", categoryId = groceries, owner = new { kind = "member", memberId = bobId } });
        await DeactivateAsync(bobId);

        await UploadOkAsync(anna, joint.Id, new TestStatement().Row("2026-11-10", -10m, "Migros"));

        var t = (await ListAsync(anna)).Single();
        await Assert.That(t.Owner.Kind).IsEqualTo("family");
        await Assert.That(t.Category?.Id).IsEqualTo(groceries);
    }

    [Test]
    public async Task A_rule_can_be_suggested_from_a_corrected_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var card = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        var eatingOut = await CategoryIdAsync(anna, "Food › Eating out");
        await UploadOkAsync(anna, card.Id, new TestStatement()
            .Row("2026-11-10", -18.50m, "CARD 1234 TRATTORIA ROMA", counterparty: "Trattoria Roma")
            .Row("2026-11-11", -4.20m, "Bäckerei Hug"));
        var list = await ListAsync(anna);
        var trattoria = list.Single(t => t.Counterparty == "Trattoria Roma");
        var bakery = list.Single(t => t.Counterparty == null);
        await PatchOkAsync(anna, trattoria.Id, new { categoryId = eatingOut });

        var draft = await anna.GetFromJsonAsync<RuleDraft>($"/api/rules/suggestion?transactionId={trattoria.Id}");
        await Assert.That(draft!.Field).IsEqualTo("counterparty");
        await Assert.That(draft.Match).IsEqualTo("equals");
        await Assert.That(draft.Text).IsEqualTo("Trattoria Roma");
        await Assert.That(draft.CategoryId).IsEqualTo(eatingOut);
        var bakeryDraft = await anna.GetFromJsonAsync<RuleDraft>($"/api/rules/suggestion?transactionId={bakery.Id}");
        await Assert.That(bakeryDraft!.Field).IsEqualTo("description");
        await Assert.That(bakeryDraft.Match).IsEqualTo("contains");
        await Assert.That(bakeryDraft.Text).IsEqualTo("Bäckerei Hug");

        // The draft can be saved as it is.
        var created = await CreateRuleAsync(anna, draft);
        await Assert.That(created.Category?.Id).IsEqualTo(eatingOut);

        var res = await bob.GetAsync($"/api/rules/suggestion?transactionId={trattoria.Id}");
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Removing_a_category_moves_or_clears_it_in_rules()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var eatingOut = await CategoryIdAsync(anna, "Food › Eating out");
        var health = await CategoryIdAsync(anna, "Health");
        var moved = await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "migros", categoryId = groceries });
        var cleared = await CreateRuleAsync(anna, new { field = "description", match = "contains", text = "pharmacy", categoryId = health, owner = Family });

        await Assert.That((await anna.DeleteAsync($"/api/categories/{groceries}?moveTo={eatingOut}")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await anna.DeleteAsync($"/api/categories/{health}?uncategorise=true")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var rules = await RulesAsync(anna);
        await Assert.That(rules.Single(r => r.Id == moved.Id).Category?.Id).IsEqualTo(eatingOut);
        await Assert.That(rules.Single(r => r.Id == cleared.Id).Category).IsNull();
    }

    // ---- helpers ----

    private static readonly object Family = new { kind = "family" };

    private static int _files;

    private static async Task UploadOkAsync(HttpClient client, Guid accountId, TestStatement content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content.ToString()));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", $"statement-{Interlocked.Increment(ref _files)}.txt");
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<Rule> CreateRuleAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/rules", body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create rule failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Rule>())!;
    }

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!.Single(c => c.Path == path).Id;

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = await MemberIdAsync(client) };

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner)
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey = "test", currency = "CHF", openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<List<Rule>> RulesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Rule>>("/api/rules"))!;

    private static async Task<Rule> PutRuleOkAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PutAsJsonAsync($"/api/rules/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Update rule failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Rule>())!;
    }

    private static async Task<RunResult> ApplyAsync(HttpClient client)
    {
        var res = await client.PostAsync("/api/rules/apply", null);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Apply failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<RunResult>())!;
    }

    private static async Task<TagDto> CreateTagAsync(HttpClient client, string name, string? dateFrom = null,
        string? dateTo = null, Guid? onlyMember = null)
    {
        var owners = onlyMember is { } id ? new[] { new { kind = "member", memberId = id } } : null;
        var res = await client.PostAsJsonAsync("/api/tags", new { name, dateFrom, dateTo, owners });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<TagDto>())!;
    }

    private async Task DeactivateAsync(Guid memberId)
    {
        using var admin = NewClient();
        await LoginAsync(admin, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
        var res = await admin.PostAsJsonAsync($"/api/admin/members/{memberId}/deactivate", new { handover = Array.Empty<object>() });
        if (res.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Deactivate failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount, string description,
        string date = "2026-11-18")
    {
        var res = await client.PostAsJsonAsync("/api/transactions", new { accountId, date, amount, description });
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

    private static async Task<Transaction> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Transaction>($"/api/transactions/{id}"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record RunResult(int Checked, int Categorised);

    private record TagDto(Guid Id, string Name);

    private record RuleDraft(string Field, string Match, string Text, Guid? CategoryId, Guid[] TagIds, OwnerDto? Owner);

    private record Me(Guid Id, string UserName);

    private record CategoryDto(Guid Id, string Path);

    private record Account(Guid Id, string Name);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record CategoryRef(Guid Id, string Name, string Path);

    private record TagRef(Guid Id, string Name, string[] Sources);

    private record Rule(Guid Id, int Priority, string Field, string Match, string Text, CategoryRef? Category,
        List<RuleTag> Tags, OwnerDto? Owner, Author Author, bool Active);

    private record RuleTag(Guid Id, string Name);

    private record Author(Guid Id, string Name);

    private record Transaction(Guid Id, DateOnly Date, decimal Amount, string Type, string Description,
        string? Counterparty, OwnerDto Owner, CategoryRef? Category, List<TagRef> Tags, string[] ManuallySet);
}
