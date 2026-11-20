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

    public ParsedStatement Read(SourceFileContent file)
    {
        var statements = Camt053Reader.Read(file, ExportHint);
        var rows = statements.SelectMany(s => s.Entries).Where(e => e.IsBooked).SelectMany(Rows).ToList();
        var last = statements.MaxBy(s => s.To);
        return new ParsedStatement(rows, statements[0].Iban,
            last?.Closing is { } closing ? new ClosingBalance(closing.Date, closing.Amount) : null);
    }

    private static IEnumerable<StatementRow> Rows(Camt053Entry entry)
    {
        var date = entry.BookingDate
            ?? throw new StatementFormatException($"{entry.Location}: the booked entry has no booking date.");
        if (entry.Details.Count <= 1)
        {
            var details = entry.Details.SingleOrDefault();
            yield return Row(entry, details, date, entry.Amount, entry.Currency, entry.AccountServicerReference);
            yield break;
        }

        foreach (var details in entry.Details)
        {
            var amount = details.Amount
                ?? throw new StatementFormatException($"{entry.Location}: a payment of the batch has no amount.");
            yield return Row(entry, details, date, amount, details.Currency ?? entry.Currency,
                details.AccountServicerReference);
        }
    }

    private static StatementRow Row(Camt053Entry entry, Camt053TransactionDetails? details, DateOnly date,
        decimal amount, string? currency, string? reference)
    {
        var counterparty = amount < 0
            ? details?.UltimateCreditor ?? details?.Creditor
            : details?.UltimateDebtor ?? details?.Debtor;
        IEnumerable<string?> remittance = details is null ? []
            : details.UnstructuredRemittance.Count > 0 ? details.UnstructuredRemittance.ToList<string?>()
            : [details.CreditorReference];
        var description = string.Join('\n',
            new[] { entry.AdditionalInfo, counterparty }.Concat(remittance)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        var foreign = details?.InstructedAmount is not null && details.InstructedCurrency != currency;
        return new StatementRow(date, amount, currency, description, counterparty, reference,
            foreign ? Math.Abs(details!.InstructedAmount!.Value) * Math.Sign(amount) : null,
            foreign ? details!.InstructedCurrency : null);
    }
}
