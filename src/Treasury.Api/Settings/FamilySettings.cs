using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Settings;

/// Settings of the Family as a whole: one row, created the first time a setting is saved.
public class FamilySettings
{
    public const int SingleId = 1;

    public int Id { get; init; } = SingleId;

    /// Only Transactions dated on or after this day count towards Amounts owed (GLOSSARY, ADR 0011).
    /// Null = every Transaction counts.
    public DateOnly? AmountOwedStartDate { get; set; }
}

public static class FamilySettingsEndpoints
{
    public record FamilySettingsResponse(DateOnly? AmountOwedStartDate);

    /// A full replacement: a missing or null `amountOwedStartDate` clears the start date.
    public record FamilySettingsRequest(DateOnly? AmountOwedStartDate);

    /// /api/settings: the Family's settings. Any Member may read and change them.
    public static void MapFamilySettings(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (TreasuryDbContext db, CancellationToken ct) =>
            Results.Ok(new FamilySettingsResponse(await db.AmountOwedStartDateAsync(ct))));

        api.MapPut("/settings", async (FamilySettingsRequest request, ClaimsPrincipal _, TreasuryDbContext db,
            CancellationToken ct) =>
        {
            var settings = await db.FamilySettings.SingleOrDefaultAsync(ct);
            if (settings is null) db.FamilySettings.Add(settings = new FamilySettings());
            settings.AmountOwedStartDate = request.AmountOwedStartDate;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new FamilySettingsResponse(settings.AmountOwedStartDate));
        });
    }

    public static Task<DateOnly?> AmountOwedStartDateAsync(this TreasuryDbContext db, CancellationToken ct = default) =>
        db.FamilySettings.AsNoTracking().Select(s => s.AmountOwedStartDate).SingleOrDefaultAsync(ct);
}
