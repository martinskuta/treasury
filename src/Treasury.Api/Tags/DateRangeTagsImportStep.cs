using Treasury.Api.Imports;

namespace Treasury.Api.Tags;

/// Classify stage: gives newly imported Transactions the date-range Tags that cover them. Registered before the
/// Rules step (#15), which runs in the same stage.
public sealed class DateRangeTagsImportStep : IImportStep
{
    public ImportStage Stage => ImportStage.Classify;

    public Task RunAsync(ImportContext context, CancellationToken ct) =>
        Tagging.ApplyDateRangesAsync(context.Db, context.Transactions);
}

public static class TagSetup
{
    public static void AddTags(this WebApplicationBuilder builder)
    {
        // Must stay the first Classify step: Rules (#15) register theirs after this one.
        builder.Services.AddScoped<IImportStep, DateRangeTagsImportStep>();
    }
}
