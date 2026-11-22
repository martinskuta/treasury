using Microsoft.EntityFrameworkCore;
using Treasury.Api.Imports;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tags;

/// Classify stage: gives newly imported Transactions the date-range Tags that cover them. Registered before the
/// Rules step (#15), which runs in the same stage.
public sealed class DateRangeTagsImportStep : IImportStep
{
    public ImportStage Stage => ImportStage.Classify;

    public Task RunAsync(ImportContext context, CancellationToken ct) =>
        Tagging.ApplyDateRangesAsync(context.Db, context.Transactions);
}

/// Classify stage: puts the Tags the Integration suggests for a row (<see cref="StatementRow.SuggestedTags"/>, e.g.
/// "fee") on the row's new Transaction. A Tag is found by name the way a Member can't create a second one with it
/// (trimmed, ignoring case); a missing one is created as a plain Tag: no date range, no Owner limit.
public sealed class SuggestedTagsImportStep : IImportStep
{
    public ImportStage Stage => ImportStage.Classify;

    public async Task RunAsync(ImportContext context, CancellationToken ct)
    {
        var db = context.Db;
        // Transactions are in the order of Rows.
        var suggested = context.Rows.Zip(context.Transactions)
            .SelectMany(pair => pair.First.SuggestedTags.Select(name => (Name: name.Trim(), TransactionId: pair.Second.Id)))
            .Where(s => s.Name.Length > 0)
            .ToList();
        if (suggested.Count == 0) return;

        var links = new List<(Guid TransactionId, Guid TagId)>();
        foreach (var byName in suggested.GroupBy(s => s.Name.ToLowerInvariant()))
        {
            var name = byName.First().Name;
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name.ToLower() == name.ToLower(), ct);
            if (tag is null)
            {
                tag = new Tag { Name = name };
                db.Tags.Add(tag);
                // Saved now (inside the import's DB transaction), so the next Account of the same import finds it.
                await db.SaveChangesAsync(ct);
            }
            links.AddRange(byName.Select(s => (s.TransactionId, tag.Id)));
        }
        await Tagging.AddAutomaticallyAsync(db, TransactionTagSource.Integration, links);
    }
}

public static class TagSetup
{
    public static void AddTags(this WebApplicationBuilder builder)
    {
        // Must stay the first Classify step: Rules (#15) register theirs after this one.
        builder.Services.AddScoped<IImportStep, DateRangeTagsImportStep>();
        builder.Services.AddScoped<IImportStep, SuggestedTagsImportStep>();
    }
}
