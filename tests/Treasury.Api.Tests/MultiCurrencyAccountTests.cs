using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Multi-currency accounts (#23, ADR-0009): a group of one Owner's single-currency Accounts with one Ingest folder.
public class MultiCurrencyAccountTests : ApiTestBase
{
    [Test]
    public async Task Integrations_say_whether_they_hold_several_currencies()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var integrations = (await anna.GetFromJsonAsync<List<Integration>>("/api/integrations"))!;

        await Assert.That(integrations.Single(i => i.Key == "test-multi").HoldsSeveralCurrencies).IsTrue();
        await Assert.That(integrations.Single(i => i.Key == "test").HoldsSeveralCurrencies).IsFalse();
    }

    private record Integration(string Key, string Name, bool CanProvideIdentifier, bool HoldsSeveralCurrencies);
}
