using System.Globalization;
using System.Text;

namespace Treasury.Api.Integrations;

/// A supported source of statements (GLOSSARY: Integration), registered by its Key (e.g. "tkb"), that knows how
/// to read its format: Source file content in, parsed statement out. Readers are pure: no database, no files, no
/// Account. Everything else (checks, duplicates, storing) is the import pipeline's job (Imports/ImportPipeline.cs).
public interface IIntegration
{
    /// Stable key stored on Accounts, e.g. "tkb", "revolut".
    string Key { get; }

    /// Name shown to Members.
    string Name { get; }

    /// Whether this Integration's statements carry an Account identifier (IBAN, last card digits) that can be
    /// checked against the Account's. Only then can an Account using it have an identifier.
    bool CanProvideIdentifier { get; }

    /// Whether one login at this provider holds balances in several currencies (e.g. "revolut"). Such an
    /// Integration is used only through a Multi-currency account, never by a plain Account (ADR-0009).
    bool HoldsSeveralCurrencies { get; }

    /// Reads a Source file. Throws <see cref="StatementFormatException"/> (with a message a Member can act on)
    /// when the content isn't a statement in this Integration's format.
    ParsedStatement Read(SourceFileContent file);
}

/// The raw content of a Source file, as uploaded or dropped into an Ingest folder.
public sealed class SourceFileContent(string fileName, byte[] bytes)
{
    /// The file's name (no folder), e.g. "statement-2026-11.csv".
    public string FileName { get; } = fileName;

    public byte[] Bytes { get; } = bytes;

    /// The content decoded as text (UTF-8 unless a byte order mark says otherwise), for text formats.
    public string ReadText(Encoding? fallback = null)
    {
        using var reader = new StreamReader(new MemoryStream(Bytes), fallback ?? new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

/// What an Integration read from a Source file.
/// <param name="Rows">The statement's movements, in file order.</param>
/// <param name="AccountIdentifier">IBAN / last card digits, when the format has one (see CanProvideIdentifier).</param>
/// <param name="ClosingBalance">The statement's closing balance, when the format has one (single-currency formats).</param>
/// <param name="BalancesByCurrency">For a format holding several currencies (Multi-currency account): the balances of
/// each currency the statement lists, also of one with no rows and a zero balance (the import decides which
/// currencies get an Account), ordered by currency code. Empty for single-currency formats, which use
/// <paramref name="ClosingBalance"/>.</param>
/// <param name="Notes">What the Integration saw in the file but didn't read (e.g. entries in a section Treasury
/// doesn't track), written for a Member.</param>
public sealed record ParsedStatement(
    IReadOnlyList<StatementRow> Rows,
    string? AccountIdentifier = null,
    ClosingBalance? ClosingBalance = null,
    IReadOnlyList<CurrencyBalances>? BalancesByCurrency = null,
    IReadOnlyList<string>? Notes = null)
{
    public IReadOnlyList<CurrencyBalances> BalancesByCurrency { get; init; } = BalancesByCurrency ?? [];

    public IReadOnlyList<string> Notes { get; init; } = Notes ?? [];
}

/// The balances a multi-currency statement reports for one currency over the statement's period (ADR-0010).
/// <param name="Currency">ISO code.</param>
/// <param name="OpeningDate">The day before the statement's period starts.</param>
/// <param name="OpeningBalance">The currency's balance at the end of <paramref name="OpeningDate"/>, i.e. before any
/// row of the statement; an import can use it to open the currency's Account.</param>
/// <param name="Closing">The currency's balance at the end of the period's last day.</param>
public sealed record CurrencyBalances(
    string Currency,
    DateOnly OpeningDate,
    decimal OpeningBalance,
    ClosingBalance Closing);

/// One movement on a statement.
/// <param name="Date">Booking date.</param>
/// <param name="Amount">In the statement's currency: positive = money in, negative = money out.</param>
/// <param name="Currency">ISO code of <paramref name="Amount"/>; null when the format doesn't say (then it is the
/// Account's currency). The pipeline rejects a file whose rows aren't in the Account's currency.</param>
/// <param name="Description">The bank's original description.</param>
/// <param name="Counterparty">The other party (merchant, payer, payee), when the format has it.</param>
/// <param name="Reference">The bank's unique reference for the movement, when the format has one.</param>
/// <param name="OriginalAmount">For a foreign-currency purchase: the amount in the original currency (same sign as
/// <paramref name="Amount"/>).</param>
/// <param name="OriginalCurrency">ISO code of <paramref name="OriginalAmount"/>.</param>
public sealed record StatementRow(
    DateOnly Date,
    decimal Amount,
    string? Currency,
    string Description,
    string? Counterparty = null,
    string? Reference = null,
    decimal? OriginalAmount = null,
    string? OriginalCurrency = null)
{
    /// Names of the Tags the Integration suggests for the row's Transaction (e.g. "fee" on a fee the bank
    /// charges); the import finds each Tag by name, or creates it, and puts it on the Transaction.
    public IReadOnlyList<string> SuggestedTags { get; init; } = [];
}

/// The balance a statement reports at the end of <paramref name="Date"/>.
public sealed record ClosingBalance(DateOnly Date, decimal Amount);

/// The content isn't a statement this Integration can read. The message is shown to Members (and written into
/// the error note next to the file in failed/), so say what is wrong and where ("line 3: ...").
public class StatementFormatException(string message) : Exception(message);

/// All registered Integrations by key. Register one with services.AddSingleton&lt;IIntegration, MyIntegration&gt;().
public sealed class IntegrationRegistry(IEnumerable<IIntegration> integrations)
{
    private readonly Dictionary<string, IIntegration> _byKey =
        integrations.ToDictionary(i => i.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<IIntegration> All => _byKey.Values;

    public IIntegration? Find(string? key) => key is not null && _byKey.TryGetValue(key, out var i) ? i : null;
}

/// How amounts are written in the messages of an import (errors and warnings).
public static class Amounts
{
    /// Swiss style like the frontend's formatAmount: 1'234.50.
    public static string Format(decimal amount) =>
        amount.ToString("#,##0.00", new NumberFormatInfo { NumberGroupSeparator = "'", NumberDecimalSeparator = "." });
}
