using Treasury.Api.Imports;

namespace Treasury.Api.Matching;

public static class MatchingSetup
{
    public static void AddMatching(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<TransactionMatcher>();
        builder.Services.AddScoped<FormerLegs>();
        builder.Services.AddScoped<IImportStep, MatchImportStep>();
    }
}

/// The pipeline's Match stage: pairs the newly imported Transactions with existing ones on other Accounts.
public sealed class MatchImportStep(TransactionMatcher matcher) : IImportStep
{
    public ImportStage Stage => ImportStage.Match;

    public Task RunAsync(ImportContext context, CancellationToken ct) => matcher.MatchAsync(context.Transactions, ct);
}
