namespace Treasury.Api.ExchangeRates;

public static class ExchangeRateSetup
{
    /// Frankfurter provider, the scheduled sync job and the CHF converter.
    /// Setting: ExchangeRates:SyncInterval (default 1 hour; nothing new = one cheap 404 from Frankfurter).
    public static void AddExchangeRates(this WebApplicationBuilder builder)
    {
        var interval = builder.Configuration.GetValue<TimeSpan?>("ExchangeRates:SyncInterval") ?? TimeSpan.FromHours(1);
        builder.Services.AddSingleton(new ExchangeRateOptions(interval));
        builder.Services.AddHttpClient<IExchangeRateProvider, FrankfurterExchangeRateProvider>(
            c => c.BaseAddress = FrankfurterExchangeRateProvider.BaseAddress);
        builder.Services.AddSingleton<ExchangeRateSyncSignal>();
        builder.Services.AddScoped<ExchangeRateSync>();
        builder.Services.AddScoped<ChfConverter>();
        builder.Services.AddHostedService<ExchangeRateJob>();
    }
}
