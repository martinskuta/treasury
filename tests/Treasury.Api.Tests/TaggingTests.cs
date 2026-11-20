using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Tags on Transactions: added by hand, applied by a date range (optionally limited to Owners), and removals that stick.
public class TaggingTests : ApiTestBase
{
    [Test]
    public async Task A_member_can_add_and_remove_any_number_of_tags_on_a_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var holiday = await CreateTagAsync(anna, new { name = "holiday" });
        var wedding = await CreateTagAsync(anna, new { name = "wedding" });
        var gifts = await CreateTagAsync(anna, new { name = "gifts" });
        var t = await AddAsync(anna, wallet.Id, -120m);

        await Assert.That(t.Tags).IsEmpty();

        var tagged = await PatchOkAsync(anna, t.Id, new { addTagIds = new[] { holiday.Id, wedding.Id, gifts.Id } });
        await Assert.That(tagged.Tags.Select(x => x.Name)).IsEquivalentTo(["gifts", "holiday", "wedding"]);
        await Assert.That(tagged.ManuallySet).Contains("tags");

        await PatchOkAsync(anna, t.Id, new { removeTagIds = new[] { wedding.Id } });
        var reloaded = await GetAsync(anna, t.Id);
        await Assert.That(reloaded.Tags.Select(x => x.Name)).IsEquivalentTo(["gifts", "holiday"]);
        await Assert.That(reloaded.Tags.Single(x => x.Name == "holiday").Sources).IsEquivalentTo(["manual"]);
    }

    [Test]
    public async Task A_tag_with_a_date_range_is_applied_to_every_transaction_in_that_range()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var before = await AddAsync(anna, wallet.Id, -10m, date: "2026-06-30", description: "Before");
        var first = await AddAsync(anna, wallet.Id, -20m, date: "2026-07-01", description: "First day");
        var last = await AddAsync(anna, wallet.Id, -30m, date: "2026-07-14", description: "Last day");
        var after = await AddAsync(anna, wallet.Id, -40m, date: "2026-07-15", description: "After");

        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });

        await Assert.That(await TagNamesAsync(anna, before.Id)).IsEmpty();
        await Assert.That(await TagNamesAsync(anna, first.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That(await TagNamesAsync(anna, last.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That(await TagNamesAsync(anna, after.Id)).IsEmpty();
        var auto = await GetAsync(anna, first.Id);
        await Assert.That(auto.Tags.Single().Sources).IsEquivalentTo(["dateRange"]);
        await Assert.That(auto.ManuallySet).DoesNotContain("tags");

        // Transactions added or re-dated later follow the range too.
        var added = await AddAsync(anna, wallet.Id, -50m, date: "2026-07-05");
        await Assert.That(added.Tags.Select(t => t.Id)).IsEquivalentTo([holiday.Id]);
        await PatchOkAsync(anna, before.Id, new { date = "2026-07-02" });
        await PatchOkAsync(anna, first.Id, new { date = "2026-08-01" });
        await Assert.That(await TagNamesAsync(anna, before.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That(await TagNamesAsync(anna, first.Id)).IsEmpty();
    }

    [Test]
    public async Task A_date_range_tag_limited_to_owners_is_applied_only_to_their_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var annas = await AddAsync(anna, joint.Id, -10m, date: "2026-07-03", owner: await MeAsync(anna));
        var bobs = await AddAsync(anna, joint.Id, -20m, date: "2026-07-03", owner: await MeAsync(bob));
        var family = await AddAsync(anna, joint.Id, -30m, date: "2026-07-03");

        var tag = await CreateTagAsync(anna, new
        {
            name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14", owners = new[] { Family, await MeAsync(bob) },
        });

        await Assert.That(await TagNamesAsync(anna, annas.Id)).IsEmpty();
        await Assert.That(await TagNamesAsync(anna, bobs.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That(await TagNamesAsync(anna, family.Id)).IsEquivalentTo(["holiday"]);

        // Handing a Transaction to another Owner moves it in or out of the limit.
        await PatchOkAsync(anna, annas.Id, new { owner = Family });
        await PatchOkAsync(anna, bobs.Id, new { owner = await MeAsync(anna) });
        await Assert.That(await TagNamesAsync(anna, annas.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That(await TagNamesAsync(anna, bobs.Id)).IsEmpty();

        // Widening the limit to every Owner tags the rest.
        await PutTagOkAsync(anna, tag.Id, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        await Assert.That(await TagNamesAsync(anna, bobs.Id)).IsEquivalentTo(["holiday"]);
    }

    [Test]
    public async Task A_date_range_tag_reaches_private_transactions_but_only_their_viewers_see_them()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var bobsCard = await CreateAccountAsync(bob, "Bob's card", await MeAsync(bob));
        var annasCard = await CreateAccountAsync(anna, "Anna's card", await MeAsync(anna));
        var bobsDinner = await AddAsync(bob, bobsCard.Id, -80m, date: "2026-07-04");
        var annasDinner = await AddAsync(anna, annasCard.Id, -60m, date: "2026-07-04");

        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });

        await Assert.That(await TagNamesAsync(bob, bobsDinner.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That((await ListAsync(anna, holiday.Id)).Select(t => t.Id)).IsEquivalentTo([annasDinner.Id]);
        await Assert.That((await ListAsync(bob, holiday.Id)).Select(t => t.Id)).IsEquivalentTo([bobsDinner.Id]);
    }

    [Test]
    public async Task Removing_an_auto_applied_tag_sticks_when_tags_are_re_applied()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var souvenir = await AddAsync(anna, wallet.Id, -15m, date: "2026-07-03", description: "Souvenir");
        var hotel = await AddAsync(anna, wallet.Id, -500m, date: "2026-07-04", description: "Hotel");
        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });

        var removed = await PatchOkAsync(anna, hotel.Id, new { removeTagIds = new[] { holiday.Id } });
        await Assert.That(removed.Tags).IsEmpty();
        await Assert.That(removed.ManuallySet).Contains("tags");

        // Re-applying the Tag (saving it, widening its range) and re-dating within the range leave it off.
        await PutTagOkAsync(anna, holiday.Id, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        await PutTagOkAsync(anna, holiday.Id, new { name = "holiday-italy", dateFrom = "2026-06-01", dateTo = "2026-07-31" });
        await PatchOkAsync(anna, hotel.Id, new { date = "2026-07-05" });

        await Assert.That(await TagNamesAsync(anna, hotel.Id)).IsEmpty();
        await Assert.That(await TagNamesAsync(anna, souvenir.Id)).IsEquivalentTo(["holiday-italy"]);
        await Assert.That((await ListAsync(anna, holiday.Id)).Select(t => t.Id)).IsEquivalentTo([souvenir.Id]);

        // Adding it back by hand works, and a later removal sticks again.
        var readded = await PatchOkAsync(anna, hotel.Id, new { addTagIds = new[] { holiday.Id } });
        await Assert.That(readded.Tags.Single().Sources).IsEquivalentTo(["manual"]);
        await PatchOkAsync(anna, hotel.Id, new { removeTagIds = new[] { holiday.Id } });
        await PutTagOkAsync(anna, holiday.Id, new { name = "holiday-italy", dateFrom = "2026-06-01", dateTo = "2026-08-31" });
        await Assert.That(await TagNamesAsync(anna, hotel.Id)).IsEmpty();
    }

    [Test]
    public async Task Changing_a_tags_date_range_updates_its_transactions_and_respects_removals()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var june = await AddAsync(anna, wallet.Id, -10m, date: "2026-06-28");
        var early = await AddAsync(anna, wallet.Id, -20m, date: "2026-07-02");
        var late = await AddAsync(anna, wallet.Id, -30m, date: "2026-07-12");
        var manual = await AddAsync(anna, wallet.Id, -40m, date: "2026-07-13");
        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        await PatchOkAsync(anna, early.Id, new { removeTagIds = new[] { holiday.Id } });
        await PatchOkAsync(anna, manual.Id, new { addTagIds = new[] { holiday.Id } });

        await PutTagOkAsync(anna, holiday.Id, new { name = "holiday", dateFrom = "2026-06-25", dateTo = "2026-07-05" });

        await Assert.That(await TagNamesAsync(anna, june.Id)).IsEquivalentTo(["holiday"]);
        await Assert.That(await TagNamesAsync(anna, early.Id)).IsEmpty();
        await Assert.That(await TagNamesAsync(anna, late.Id)).IsEmpty();
        // A Tag added by hand stays, whatever the range does.
        await Assert.That(await TagNamesAsync(anna, manual.Id)).IsEquivalentTo(["holiday"]);

        // Without a range the Tag only stays where it was added by hand.
        await PutTagOkAsync(anna, holiday.Id, new { name = "holiday" });
        await Assert.That((await ListAsync(anna, holiday.Id)).Select(t => t.Id)).IsEquivalentTo([manual.Id]);
    }

    [Test]
    public async Task Imported_transactions_get_the_date_range_tags_that_cover_them()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var card = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        var familyOnly = await CreateTagAsync(anna, new
        {
            name = "family-trip", dateFrom = "2026-07-01", dateTo = "2026-07-14", owners = new[] { Family },
        });
        await CreateTagAsync(anna, new { name = "unrelated", dateFrom = "2025-01-01", dateTo = "2025-01-31" });

        await UploadOkAsync(anna, card.Id, "july.txt", new TestStatement()
            .Row("2026-06-30", -10m, "Before")
            .Row("2026-07-03", -20m, "Pizza")
            .Row("2026-07-14", -30m, "Gelato"));

        var list = await ListAsync(anna);
        await Assert.That(list.Single(t => t.Description == "Before").Tags).IsEmpty();
        await Assert.That(list.Single(t => t.Description == "Pizza").Tags.Select(t => t.Name)).IsEquivalentTo(["holiday"]);
        await Assert.That(list.Single(t => t.Description == "Gelato").Tags.Single().Sources).IsEquivalentTo(["dateRange"]);
        await Assert.That(await ListAsync(anna, familyOnly.Id)).IsEmpty();
        await Assert.That(await ListAsync(anna, holiday.Id)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Tags_can_be_given_when_adding_a_transaction_and_unknown_tags_are_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var gifts = await CreateTagAsync(anna, new { name = "gifts" });

        var t = await AddAsync(anna, wallet.Id, -25m, tagIds: [gifts.Id]);
        await Assert.That(t.Tags.Select(x => x.Name)).IsEquivalentTo(["gifts"]);
        await Assert.That(t.ManuallySet).Contains("tags");

        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = wallet.Id, date = "2026-11-18", amount = -1m, description = "x", tagIds = new[] { Guid.NewGuid() } }),
            "tagIds");
        await AssertValidationErrorAsync(await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}",
            new { addTagIds = new[] { Guid.NewGuid() } }), "addTagIds");
        await AssertValidationErrorAsync(await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}",
            new { removeTagIds = new[] { Guid.NewGuid() } }), "removeTagIds");
        await AssertValidationErrorAsync(await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}",
            new { addTagIds = new[] { gifts.Id }, removeTagIds = new[] { gifts.Id } }), "removeTagIds");
        await Assert.That(await TagNamesAsync(anna, t.Id)).IsEquivalentTo(["gifts"]);
    }

    [Test]
    public async Task Only_members_who_see_a_transaction_can_tag_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var annasCard = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        var shared = await AddAsync(anna, joint.Id, -40m);
        var secret = await AddAsync(anna, annasCard.Id, -40m);
        var gifts = await CreateTagAsync(anna, new { name = "gifts" });

        await PatchOkAsync(bob, shared.Id, new { addTagIds = new[] { gifts.Id } });
        var res = await bob.PatchAsJsonAsync($"/api/transactions/{secret.Id}", new { addTagIds = new[] { gifts.Id } });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await TagNamesAsync(anna, shared.Id)).IsEquivalentTo(["gifts"]);
        await Assert.That(await TagNamesAsync(anna, secret.Id)).IsEmpty();
    }

    [Test]
    public async Task Deleting_a_tag_takes_it_off_every_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });
        var gifts = await CreateTagAsync(anna, new { name = "gifts" });
        var t = await AddAsync(anna, wallet.Id, -25m, date: "2026-07-02", tagIds: [gifts.Id]);
        var other = await AddAsync(anna, wallet.Id, -25m, date: "2026-07-03");
        await PatchOkAsync(anna, other.Id, new { removeTagIds = new[] { holiday.Id } });

        await anna.DeleteAsync($"/api/tags/{holiday.Id}");

        await Assert.That(await TagNamesAsync(anna, t.Id)).IsEquivalentTo(["gifts"]);
        await Assert.That(await TagNamesAsync(anna, other.Id)).IsEmpty();
    }

    // ---- helpers ----

    private static async Task UploadOkAsync(HttpClient client, Guid accountId, string fileName, object content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content.ToString()!));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<Tag> PutTagOkAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PutAsJsonAsync($"/api/tags/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Update tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Tag>())!;
    }

    private static async Task<string[]> TagNamesAsync(HttpClient client, Guid transactionId) =>
        (await GetAsync(client, transactionId)).Tags.Select(t => t.Name).ToArray();

    private static readonly object Family = new { kind = "family" };

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = await MemberIdAsync(client) };

    private static async Task<Tag> CreateTagAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/tags", body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Tag>())!;
    }

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

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount,
        string date = "2026-11-18", string description = "Something", object? owner = null, Guid[]? tagIds = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions", new { accountId, date, amount, description, owner, tagIds });
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

    private static async Task<List<Transaction>> ListAsync(HttpClient client, Guid? tagId = null) =>
        (await client.GetFromJsonAsync<List<Transaction>>(tagId is null ? "/api/transactions" : $"/api/transactions?tagId={tagId}"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id, string UserName);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Account(Guid Id, string Name);

    private record Tag(Guid Id, string Name);

    private record TagRef(Guid Id, string Name, string[] Sources);

    private record Transaction(Guid Id, DateOnly Date, decimal Amount, string Description, OwnerDto Owner,
        List<TagRef> Tags, string[] ManuallySet);
}
