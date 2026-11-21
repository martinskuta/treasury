namespace Treasury.Api.Integrations;

/// "TKB" (key tkb): Thurgauer Kantonalbank's ISO 20022 camt.053 statement, as a single XML file or a zip with one
/// or more of them (documented in README "TKB"). Reading and checking camt.053 is <see cref="Camt053Reader"/>'s job;
/// this class turns its statements into rows and owns the description and counterparty composition.
/// TKB's CSV export is not supported.
public sealed class TkbIntegration : IIntegration
{
    private const string ExportHint = "Export the statement as camt.053 (XML or zip) from TKB e-banking.";

    public string Key => "tkb";

    public string Name => "TKB";

    public bool CanProvideIdentifier => true;

    public bool HoldsSeveralCurrencies => false;

    public ParsedStatement Read(SourceFileContent file)
    {
        var statements = Camt053Reader.Read(file, ExportHint);
        var rows = statements.SelectMany(s => s.Entries).Where(e => e.IsBooked).SelectMany(Rows).ToList();
        var closing = statements.MaxBy(s => s.To)!.Closing;
        return new ParsedStatement(rows, statements[0].Iban, new ClosingBalance(closing.Date, closing.Amount));
    }

    /// One row per booked entry, except a batch (several transaction details) whose details all have amounts that
    /// add up to the entry's amount: that becomes one row per detail.
    private static IEnumerable<StatementRow> Rows(Camt053Entry entry)
    {
        var date = entry.BookingDate
            ?? throw new StatementFormatException($"{entry.Location}: there is no booking date (BookgDt).");
        if (IsSplittableBatch(entry))
            return entry.Details.Select(details => Row(entry, details, date, details.Amount!.Value, details.IsCredit,
                details.Currency ?? entry.Currency, details.AccountServicerReference, details.InstructedAmount));

        var first = entry.Details.FirstOrDefault();
        var instructed = entry.Details.Count == 1 ? first!.InstructedAmount ?? entry.InstructedAmount : entry.InstructedAmount;
        return [Row(entry, first, date, entry.Amount, entry.IsCredit, entry.Currency, entry.AccountServicerReference, instructed)];
    }

    private static bool IsSplittableBatch(Camt053Entry entry) =>
        entry.Details.Count > 1
        && entry.Details.All(d => d.Amount is not null)
        && entry.Details.Sum(d => d.Amount!.Value) == entry.Amount;

    private static StatementRow Row(Camt053Entry entry, Camt053TransactionDetails? details, DateOnly date,
        decimal amount, bool isCredit, string? currency, string? reference, Camt053Amount? instructed)
    {
        var counterparty = isCredit
            ? details?.UltimateDebtor ?? details?.Debtor
            : details?.UltimateCreditor ?? details?.Creditor;
        IEnumerable<string?> remittance = details is null ? []
            : details.UnstructuredRemittance.Count > 0 ? details.UnstructuredRemittance.ToList<string?>()
            : [details.CreditorReference];
        var description = string.Join('\n',
            new[] { entry.AdditionalInfo, counterparty }.Concat(remittance)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        var original = instructed is { Currency: { } instructedCurrency } && instructedCurrency != currency
            ? instructed
            : null;
        return new StatementRow(date, amount, currency, description, counterparty, reference,
            original?.Amount, original?.Currency);
    }
}
