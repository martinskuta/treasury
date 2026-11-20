using Microsoft.EntityFrameworkCore;
using Treasury.Api.Integrations;

namespace Treasury.Api.Imports;

/// Duplicate detection (spec #1, #9): drops statement rows the Account already holds, so importing overlapping
/// statements or the same file twice is always safe.
///
/// Duplicate key: Account + bank reference when the row has one; otherwise Account + date + amount + original
/// description (the currency is the Account's, so the Account covers it). Matches are counted per key: with n matching
/// Transactions in the database and m rows in the file, max(0, m - n) rows are imported. Rows are only compared with
/// the database, never with each other (two 4.50 CHF coffees on one day are two Transactions).
/// Every Transaction of the Account counts, manual ones included.
public sealed class DuplicateDetection : IImportStep
{
    public ImportStage Stage => ImportStage.Deduplicate;

    public async Task RunAsync(ImportContext context, CancellationToken ct)
    {
        var rows = context.Rows;
        if (rows.Count == 0) return;
        var accountId = context.Account.Id;
        var stored = new Dictionary<DuplicateKey, int>();

        var references = rows.Select(r => Reference(r)).OfType<string>().Distinct().ToList();
        if (references.Count > 0)
        {
            var byReference = await context.Db.Transactions
                .Where(t => t.AccountId == accountId && t.Reference != null && references.Contains(t.Reference))
                .GroupBy(t => t.Reference!)
                .Select(g => new { Reference = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var r in byReference) stored[DuplicateKey.ForReference(r.Reference)] = r.Count;
        }

        var withoutReference = rows.Where(r => Reference(r) is null).ToList();
        if (withoutReference.Count > 0)
        {
            var dates = withoutReference.Select(r => r.Date).Distinct().ToList();
            var byFacts = await context.Db.Transactions
                .Where(t => t.AccountId == accountId && dates.Contains(t.Date))
                .GroupBy(t => new { t.Date, t.Amount, t.Description })
                .Select(g => new { g.Key.Date, g.Key.Amount, g.Key.Description, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var f in byFacts) stored[DuplicateKey.ForFacts(f.Date, f.Amount, f.Description)] = f.Count;
        }

        // Each stored Transaction cancels out one row with its key; the rest are new.
        var kept = new List<StatementRow>(rows.Count);
        foreach (var row in rows)
        {
            var key = KeyOf(row);
            if (stored.TryGetValue(key, out var n) && n > 0)
            {
                stored[key] = n - 1;
                context.SkippedCount++;
            }
            else
            {
                kept.Add(row);
            }
        }
        context.Rows = kept;
    }

    // Keys use the values as they are stored (the pipeline trims description and reference).
    private static string? Reference(StatementRow row) =>
        string.IsNullOrWhiteSpace(row.Reference) ? null : row.Reference.Trim();

    private static DuplicateKey KeyOf(StatementRow row) =>
        Reference(row) is { } reference
            ? DuplicateKey.ForReference(reference)
            : DuplicateKey.ForFacts(row.Date, row.Amount, row.Description.Trim());

    private readonly record struct DuplicateKey(string? Reference, DateOnly Date, decimal Amount, string? Description)
    {
        public static DuplicateKey ForReference(string reference) => new(reference, default, 0, null);

        // decimal equality and hash ignore scale (4.5 == 4.50), so stored numeric(19,4) values match parsed ones.
        public static DuplicateKey ForFacts(DateOnly date, decimal amount, string description) =>
            new(null, date, amount, description);
    }
}
