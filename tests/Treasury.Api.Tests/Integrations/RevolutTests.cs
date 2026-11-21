using System.Text;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests.Integrations;

/// Seam 2: the revolut reader (account statement CSV, English), Source file content in, parsed statement out
/// (samples in Revolut/).
public class RevolutTests
{
    private static readonly RevolutIntegration Reader = new();

    private const string OctoberSample = "account-statement_2026-10-01_2026-10-31_en-us_6a51a3.csv";
    private const string NovemberSample = "account-statement_2026-11-01_2026-11-19_en-us_affca4.csv";

    private static ParsedStatement ReadSample(string name) => Reader.Read(new SourceFileContent(name,
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "Revolut", name))));

    [Test]
    public async Task It_is_registered_as_revolut_without_an_identifier_and_with_several_currencies()
    {
        await Assert.That(Reader.Key).IsEqualTo("revolut");
        await Assert.That(Reader.Name).IsEqualTo("Revolut");
        await Assert.That(Reader.CanProvideIdentifier).IsFalse();
        await Assert.That(Reader.HoldsSeveralCurrencies).IsTrue();
    }

    [Test]
    public async Task Reads_the_october_sample()
    {
        var statement = ReadSample(OctoberSample);

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 10, 19), -31.40m, "CHF", "Playtomic", "Playtomic"),
            new StatementRow(new DateOnly(2026, 10, 25), -18.90m, "CHF", "Playtomic", "Playtomic"),
        });
        await Assert.That(statement.AccountIdentifier).IsNull();
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", new DateOnly(2026, 10, 19), 2534.73m,
                new ClosingBalance(new DateOnly(2026, 10, 25), 2484.43m)),
        });
    }

    [Test]
    public async Task Reads_the_november_sample_continuing_october_and_skipping_the_pending_payment()
    {
        var statement = ReadSample(NovemberSample);

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 4), 50.00m, "CHF", "Revolut Bank UAB", "Revolut Bank UAB"),
            new StatementRow(new DateOnly(2026, 11, 5), -42.80m, "CHF", "ÖMI'S", "ÖMI'S"),
            new StatementRow(new DateOnly(2026, 11, 5), -129.95m, "CHF", "Tmnábytek.cz", "Tmnábytek.cz"),
            new StatementRow(new DateOnly(2026, 11, 9), -1500.00m, "CHF", "To Jan Novak", "Jan Novak"),
        });
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            // October closed at 2484.43, so November starts where October ended.
            new CurrencyBalances("CHF", new DateOnly(2026, 11, 4), 2484.43m,
                new ClosingBalance(new DateOnly(2026, 11, 9), 861.68m)),
        });
    }
}
