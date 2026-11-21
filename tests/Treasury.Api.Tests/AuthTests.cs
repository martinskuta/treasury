using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

public class AuthTests : ApiTestBase
{
    [Test]
    public async Task First_admin_from_environment_can_log_in_and_holds_the_admin_role()
    {
        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { userName = TreasuryApiFactory.AdminUserName, password = TreasuryApiFactory.AdminPassword });

        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var me = await Client.GetFromJsonAsync<Me>("/api/auth/me");
        await Assert.That(me!.UserName).IsEqualTo(TreasuryApiFactory.AdminUserName);
        await Assert.That(me.Roles).Contains("Admin");
    }

    [Test]
    public async Task Login_with_a_wrong_password_is_rejected()
    {
        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { userName = TreasuryApiFactory.AdminUserName, password = "Wrong-Pa55word" });

        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await Client.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Signed_out_requests_to_the_api_get_401_not_a_redirect()
    {
        var res = await Client.GetAsync("/api/auth/me");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task After_logout_the_session_no_longer_works()
    {
        await LoginAsAdminAsync();

        var logout = await Client.PostAsync("/api/auth/logout", null);

        await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await Client.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_member_can_change_their_own_password()
    {
        await LoginAsAdminAsync();

        var change = await Client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = TreasuryApiFactory.AdminPassword, newPassword = "New-Pa55word" });

        await Assert.That(change.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await Client.GetAsync("/api/auth/me")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var other = NewClient();
        var oldLogin = await other.PostAsJsonAsync("/api/auth/login",
            new { userName = TreasuryApiFactory.AdminUserName, password = TreasuryApiFactory.AdminPassword });
        await Assert.That(oldLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await LoginAsync(other, TreasuryApiFactory.AdminUserName, "New-Pa55word");
    }

    [Test]
    public async Task Changing_the_password_needs_the_current_password()
    {
        await LoginAsAdminAsync();

        var change = await Client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = "Wrong-Pa55word", newPassword = "New-Pa55word" });

        await Assert.That(change.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var other = NewClient();
        await LoginAsync(other, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
    }

    [Test]
    public async Task Changing_the_password_requires_being_logged_in()
    {
        var change = await Client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = TreasuryApiFactory.AdminPassword, newPassword = "New-Pa55word" });

        await Assert.That(change.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task First_admin_is_only_created_once_and_a_restart_keeps_the_changed_password()
    {
        await LoginAsAdminAsync();
        await Client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = TreasuryApiFactory.AdminPassword, newPassword = "New-Pa55word" });

        // Second startup on the same database, still with the Admin environment variables set.
        await using var restarted = new TreasuryApiFactory(ConnectionString);
        using var client = restarted.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });

        var oldLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = TreasuryApiFactory.AdminUserName, password = TreasuryApiFactory.AdminPassword });
        await Assert.That(oldLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await LoginAsync(client, TreasuryApiFactory.AdminUserName, "New-Pa55word");
    }

    [Test]
    public async Task A_session_survives_an_api_restart()
    {
        using var raw = App.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        var login = await raw.PostAsJsonAsync("/api/auth/login",
            new { userName = TreasuryApiFactory.AdminUserName, password = TreasuryApiFactory.AdminPassword });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("treasury.auth=")).Split(';')[0];

        await using var restarted = new TreasuryApiFactory(ConnectionString);
        using var client = restarted.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me") { Headers = { { "Cookie", cookie } } };

        await Assert.That((await client.SendAsync(request)).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_member_who_is_not_an_admin_logs_in_without_the_admin_role()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var me = await anna.GetFromJsonAsync<Me>("/api/auth/me");

        await Assert.That(me!.UserName).IsEqualTo("anna");
        await Assert.That(me.Roles).IsEmpty();
    }

    private sealed record Me(string UserName, List<string> Roles);
}
