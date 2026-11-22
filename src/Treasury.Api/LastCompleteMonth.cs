namespace Treasury.Api;

/// THE last complete calendar month: the one before the month of the clock's UTC date. The recent months report
/// caps its end month at it and an Account lags when its reach stops before its last day (#37); both ask here so
/// "end month" and "lagging" can't drift.
public static class LastCompleteMonth
{
    public static DateOnly FirstDay(TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    }

    public static DateOnly LastDay(TimeProvider clock) => FirstDay(clock).AddMonths(1).AddDays(-1);
}
