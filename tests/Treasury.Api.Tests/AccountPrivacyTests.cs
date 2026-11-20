using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// US37: a Member sees a Transaction reassigned to them from another Member's personal Account "without exposing
/// the rest of that Account" — no Account id or name, no Source file.
public class AccountPrivacyTests : ApiTestBase
{
    [Test]
    public async Task A_transaction_reassigned_from_another_members_account_hides_that_account_and_its_source_file()
    {
        using var alice = await LoginAsNewMemberAsync("alice");
        using var bob = await LoginAsNewMemberAsync("bob");
        var alicesCard = await CreateAccountAsync(alice, "Alice card", await MeAsync(alice));
        await UploadOkAsync(alice, alicesCard.Id, new TestStatement().Row("2026-11-18", -80m, "CONCERT TICKET"));
        var ticket = (await ListAsync(alice)).Single();
        var res = await alice.PatchAsJsonAsync($"/api/transactions/{ticket.Id}", new { owner = await MeAsync(bob) });
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var seenByBob = (await ListAsync(bob)).Single();
        await Assert.That(seenByBob.AccountHidden).IsTrue();
        await Assert.That(seenByBob.AccountId).IsNull();
        await Assert.That(seenByBob.AccountName).IsNull();
        await Assert.That(seenByBob.SourceFile).IsNull();
        var single = (await bob.GetFromJsonAsync<Transaction>($"/api/transactions/{ticket.Id}"))!;
        await Assert.That(single.AccountId).IsNull();
        await Assert.That(single.SourceFile).IsNull();

        var seenByAlice = (await ListAsync(alice)).Single();
        await Assert.That(seenByAlice.AccountHidden).IsFalse();
        await Assert.That(seenByAlice.AccountId).IsEqualTo(alicesCard.Id);
        await Assert.That(seenByAlice.AccountName).IsEqualTo("Alice card");
        await Assert.That(seenByAlice.SourceFile).IsNotNull();
    }

    [Test]
    public async Task The_other_leg_of_a_pair_names_its_account_only_when_the_viewer_can_see_it()
    {
        using var alice = await LoginAsNewMemberAsync("alice");
        using var bob = await LoginAsNewMemberAsync("bob");
        var alicesCard = await CreateAccountAsync(alice, "Alice card", await MeAsync(alice));
        var bobsCard = await CreateAccountAsync(bob, "Bob card", await MeAsync(bob));
        var household = await CreateAccountAsync(alice, "Household", new { kind = "family" });
        await AddAsync(bob, bobsCard.Id, -50m, "TO ALICE");
        await AddAsync(alice, alicesCard.Id, 50m, "FROM BOB");
        await AddAsync(alice, alicesCard.Id, -300m, "TO HOUSEHOLD");
        await AddAsync(alice, household.Id, 300m, "FROM ALICE");

        var alices = await ListAsync(alice);
        var fromBob = alices.Single(t => t.Description == "FROM BOB");
        await Assert.That(fromBob.Link!.Kind).IsEqualTo("contribution");
        await Assert.That(fromBob.Link.AccountHidden).IsTrue();
        await Assert.That(fromBob.Link.AccountName).IsNull();
        var toHousehold = alices.Single(t => t.Description == "TO HOUSEHOLD");
        await Assert.That(toHousehold.Link!.AccountHidden).IsFalse();
        await Assert.That(toHousehold.Link.AccountName).IsEqualTo("Household");

        var toAlice = (await ListAsync(bob)).Single(t => t.Description == "TO ALICE");
        await Assert.That(toAlice.Link!.AccountHidden).IsTrue();
        await Assert.That(toAlice.Link.AccountName).IsNull();
    }

    // ---- helpers ----

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

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

    private static async Task AddAsync(HttpClient client, Guid accountId, decimal amount, string description)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date = "2026-11-18", amount, description });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task UploadOkAsync(HttpClient client, Guid accountId, TestStatement statement)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(statement.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "statement.txt");
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record SourceFileRef(Guid Id, string FileName);

    private record LinkDto(string Kind, Guid TransactionId, string? AccountName, bool AccountHidden);

    private record Transaction(Guid Id, Guid? AccountId, string? AccountName, bool AccountHidden, string Description,
        SourceFileRef? SourceFile, LinkDto? Link);
}
