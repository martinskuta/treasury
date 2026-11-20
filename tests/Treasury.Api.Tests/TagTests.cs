using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Tag management: shared Tags with an optional date range and Owner limit.
public class TagTests : ApiTestBase
{
    [Test]
    public async Task A_tag_created_by_one_member_is_shared_with_the_whole_family()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var created = await CreateTagAsync(anna, new { name = "  holiday-italy-2026 " });

        await Assert.That(created.Name).IsEqualTo("holiday-italy-2026");
        await Assert.That(created.DateFrom).IsNull();
        var bobsTags = await bob.GetFromJsonAsync<List<Tag>>("/api/tags");
        await Assert.That(bobsTags!.Select(t => t.Name)).IsEquivalentTo(["holiday-italy-2026"]);
    }

    [Test]
    public async Task Any_member_can_rename_and_delete_a_tag()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var tag = await CreateTagAsync(anna, new { name = "holiday" });
        var other = await CreateTagAsync(anna, new { name = "wedding" });

        var renamed = await bob.PutAsJsonAsync($"/api/tags/{tag.Id}", new { name = "holiday-italy-2026" });
        await Assert.That(renamed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await renamed.Content.ReadFromJsonAsync<Tag>())!.Name).IsEqualTo("holiday-italy-2026");

        var deleted = await bob.DeleteAsync($"/api/tags/{other.Id}");
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var list = await anna.GetFromJsonAsync<List<Tag>>("/api/tags");
        await Assert.That(list!.Select(t => t.Name)).IsEquivalentTo(["holiday-italy-2026"]);
        await Assert.That((await bob.DeleteAsync($"/api/tags/{other.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.PutAsJsonAsync($"/api/tags/{other.Id}", new { name = "x" })).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_tag_needs_a_name_that_no_other_tag_has()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var tag = await CreateTagAsync(anna, new { name = "Holiday" });
        await CreateTagAsync(anna, new { name = "wedding" });

        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags", new { name = " " }), "name");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags", new { name = "holiday" }), "name");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags", new { name = new string('x', 101) }), "name");
        await AssertValidationErrorAsync(await anna.PutAsJsonAsync($"/api/tags/{tag.Id}", new { name = "WEDDING" }), "name");
        // Changing only the case of its own name is fine.
        var res = await anna.PutAsJsonAsync($"/api/tags/{tag.Id}", new { name = "holiday" });
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_date_range_needs_both_ends_in_order_and_an_owner_limit_needs_a_date_range()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags",
            new { name = "a", dateFrom = "2026-07-01" }), "dateTo");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags",
            new { name = "a", dateTo = "2026-07-01" }), "dateFrom");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags",
            new { name = "a", dateFrom = "2026-07-10", dateTo = "2026-07-01" }), "dateTo");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags",
            new { name = "a", owners = new[] { Family } }), "owners");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/tags",
            new { name = "a", dateFrom = "2026-07-01", dateTo = "2026-07-10", owners = new[] { new { kind = "member", memberId = Guid.NewGuid() } } }),
            "owners");
    }

    [Test]
    public async Task A_tag_keeps_its_date_range_and_owner_limit()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);

        var tag = await CreateTagAsync(anna, new
        {
            name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14", owners = new[] { Family, me, Family },
        });

        await Assert.That(tag.DateFrom).IsEqualTo(new DateOnly(2026, 7, 1));
        await Assert.That(tag.DateTo).IsEqualTo(new DateOnly(2026, 7, 14));
        await Assert.That(tag.Owners.Select(o => o.Name)).IsEquivalentTo(["Family", "anna"]);

        var cleared = await anna.PutAsJsonAsync($"/api/tags/{tag.Id}", new { name = "holiday" });
        var reloaded = (await cleared.Content.ReadFromJsonAsync<Tag>())!;
        await Assert.That(reloaded.DateFrom).IsNull();
        await Assert.That(reloaded.Owners).IsEmpty();
    }

    [Test]
    public async Task Tags_require_login()
    {
        await Assert.That((await Client.GetAsync("/api/tags")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private static readonly object Family = new { kind = "family" };

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id, string UserName);

    private static async Task<Tag> CreateTagAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/tags", body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Tag>())!;
    }

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Tag(Guid Id, string Name, DateOnly? DateFrom, DateOnly? DateTo, List<OwnerDto> Owners);
}
