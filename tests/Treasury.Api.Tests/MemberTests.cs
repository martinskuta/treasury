using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

public class MemberTests : ApiTestBase
{
    [Test]
    public async Task The_admin_role_in_the_session_cookie_opens_member_management()
    {
        await LoginAsAdminAsync();

        var members = await Client.GetFromJsonAsync<List<MemberRow>>("/api/admin/members");

        await Assert.That(members!.Select(m => m.UserName)).IsEquivalentTo([TreasuryApiFactory.AdminUserName]);
        await Assert.That(members[0].IsAdmin).IsTrue();
        await Assert.That(members[0].IsActive).IsTrue();
    }

    [Test]
    public async Task An_admin_can_add_a_member_who_can_then_log_in()
    {
        await LoginAsAdminAsync();

        var res = await Client.PostAsJsonAsync("/api/admin/members", new { userName = "anna", password = "Anna-Pa55word" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = await res.Content.ReadFromJsonAsync<MemberRow>();
        await Assert.That(created!.UserName).IsEqualTo("anna");
        await Assert.That(created.IsAdmin).IsFalse();
        await Assert.That(created.IsActive).IsTrue();
        using var anna = NewClient();
        await LoginAsync(anna, "anna", "Anna-Pa55word");
        var me = await anna.GetFromJsonAsync<Me>("/api/auth/me");
        await Assert.That(me!.Id).IsEqualTo(created.Id);
        await Assert.That(me.Roles).IsEmpty();
    }

    [Test]
    public async Task Adding_a_member_with_a_taken_user_name_or_weak_password_is_rejected()
    {
        await LoginAsAdminAsync();

        var taken = await Client.PostAsJsonAsync("/api/admin/members",
            new { userName = TreasuryApiFactory.AdminUserName, password = "Anna-Pa55word" });
        var weak = await Client.PostAsJsonAsync("/api/admin/members", new { userName = "anna", password = "short" });
        var blank = await Client.PostAsJsonAsync("/api/admin/members", new { userName = " ", password = "Anna-Pa55word" });

        await AssertValidationErrorAsync(taken, "userName");
        await AssertValidationErrorAsync(weak, "password");
        await AssertValidationErrorAsync(blank, "userName");
        var members = await Client.GetFromJsonAsync<List<MemberRow>>("/api/admin/members");
        await Assert.That(members!).Count().IsEqualTo(1);
    }

    // {id} is replaced by another Member's id.
    [Test]
    [Arguments("GET", "/api/admin/members")]
    [Arguments("POST", "/api/admin/members")]
    public async Task Members_without_the_admin_role_cannot_reach_member_management(string method, string path)
    {
        var bobId = await AddMemberAsync("bob");
        using var anna = await LoginAsNewMemberAsync("anna");
        using var signedOut = NewClient();
        HttpRequestMessage Request() => new(new HttpMethod(method), path.Replace("{id}", bobId.ToString()))
        {
            Content = JsonContent.Create(new { userName = "eve", password = "Eve-Pa55word", newPassword = "Eve-Pa55word", isAdmin = true }),
        };

        var asMember = await anna.SendAsync(Request());
        var asNobody = await signedOut.SendAsync(Request());

        await Assert.That(asMember.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(asNobody.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await LoginAsAdminAsync();
        var members = await Client.GetFromJsonAsync<List<MemberRow>>("/api/admin/members");
        await Assert.That(members!.Select(m => m.UserName)).IsEquivalentTo(["admin", "anna", "bob"]);
        await Assert.That(members.Single(m => m.UserName == "bob").IsAdmin).IsFalse();
        await Assert.That(members.Single(m => m.UserName == "bob").IsActive).IsTrue();
    }

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<Problem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record Problem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id, string UserName, List<string> Roles);

    private record MemberRow(Guid Id, string UserName, bool IsAdmin, bool IsActive);
}
