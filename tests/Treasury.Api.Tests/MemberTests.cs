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

    [Test]
    public async Task An_admin_can_reset_a_members_password_which_ends_their_sessions()
    {
        var annaId = await AddMemberAsync("anna");
        using var annaSession = NewClient();
        await LoginAsync(annaSession, "anna", MemberPassword);
        await LoginAsAdminAsync();

        var res = await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/password", new { newPassword = "Reset-Pa55word" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var anna = NewClient();
        var oldLogin = await anna.PostAsJsonAsync("/api/auth/login", new { userName = "anna", password = MemberPassword });
        await Assert.That(oldLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await LoginAsync(anna, "anna", "Reset-Pa55word");
        await Assert.That((await annaSession.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Resetting_to_a_weak_password_is_rejected_and_unknown_members_are_404()
    {
        var annaId = await AddMemberAsync("anna");
        await LoginAsAdminAsync();

        var weak = await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/password", new { newPassword = "short" });
        var unknown = await Client.PostAsJsonAsync($"/api/admin/members/{Guid.NewGuid()}/password", new { newPassword = "Reset-Pa55word" });

        await AssertValidationErrorAsync(weak, "newPassword");
        await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var anna = NewClient();
        await LoginAsync(anna, "anna", MemberPassword);
    }

    [Test]
    public async Task An_admin_can_give_and_remove_the_admin_role_effective_in_open_sessions()
    {
        var annaId = await AddMemberAsync("anna");
        using var anna = NewClient();
        await LoginAsync(anna, "anna", MemberPassword);
        await LoginAsAdminAsync();

        var give = await Client.PutAsJsonAsync($"/api/admin/members/{annaId}/admin", new { isAdmin = true });

        await Assert.That(give.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await give.Content.ReadFromJsonAsync<MemberRow>())!.IsAdmin).IsTrue();
        await Assert.That((await anna.GetAsync("/api/admin/members")).StatusCode).IsEqualTo(HttpStatusCode.OK);

        var remove = await Client.PutAsJsonAsync($"/api/admin/members/{annaId}/admin", new { isAdmin = false });

        await Assert.That(remove.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await remove.Content.ReadFromJsonAsync<MemberRow>())!.IsAdmin).IsFalse();
        await Assert.That((await anna.GetAsync("/api/admin/members")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That((await anna.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task The_last_admin_cannot_lose_the_admin_role()
    {
        await LoginAsAdminAsync();
        var adminId = (await Client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

        var res = await Client.PutAsJsonAsync($"/api/admin/members/{adminId}/admin", new { isAdmin = false });

        await AssertValidationErrorAsync(res, "isAdmin");
        var me = await Client.GetFromJsonAsync<Me>("/api/auth/me");
        await Assert.That(me!.Roles).Contains("Admin");
    }

    [Test]
    public async Task An_admin_can_step_down_while_another_admin_remains()
    {
        var annaId = await AddMemberAsync("anna");
        await LoginAsAdminAsync();
        var adminId = (await Client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        await Client.PutAsJsonAsync($"/api/admin/members/{annaId}/admin", new { isAdmin = true });

        var res = await Client.PutAsJsonAsync($"/api/admin/members/{adminId}/admin", new { isAdmin = false });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await Client.GetAsync("/api/admin/members")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task A_deactivated_member_can_no_longer_log_in_and_their_open_session_stops_working()
    {
        var annaId = await AddMemberAsync("anna");
        using var annaSession = NewClient();
        await LoginAsync(annaSession, "anna", MemberPassword);
        await LoginAsAdminAsync();

        var res = await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/deactivate", new { handover = Array.Empty<object>() });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await annaSession.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await annaSession.GetAsync("/api/accounts")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var again = NewClient();
        var login = await again.PostAsJsonAsync("/api/auth/login", new { userName = "anna", password = MemberPassword });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var members = await Client.GetFromJsonAsync<List<MemberRow>>("/api/admin/members");
        var anna = members!.Single(m => m.UserName == "anna");
        await Assert.That(anna.IsActive).IsFalse();
    }

    [Test]
    public async Task A_password_reset_does_not_reactivate_a_deactivated_member()
    {
        var annaId = await AddMemberAsync("anna");
        await LoginAsAdminAsync();
        await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/deactivate", new { handover = Array.Empty<object>() });

        await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/password", new { newPassword = "Reset-Pa55word" });

        using var anna = NewClient();
        var login = await anna.PostAsJsonAsync("/api/auth/login", new { userName = "anna", password = "Reset-Pa55word" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task An_admin_cannot_deactivate_themselves()
    {
        await LoginAsAdminAsync();
        var adminId = (await Client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

        var res = await Client.PostAsJsonAsync($"/api/admin/members/{adminId}/deactivate", new { handover = Array.Empty<object>() });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await Client.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // {id} is replaced by another Member's id.
    [Test]
    [Arguments("GET", "/api/admin/members")]
    [Arguments("POST", "/api/admin/members")]
    [Arguments("POST", "/api/admin/members/{id}/password")]
    [Arguments("PUT", "/api/admin/members/{id}/admin")]
    [Arguments("POST", "/api/admin/members/{id}/deactivate")]
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
