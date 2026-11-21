using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Accounts;
using Treasury.Api.Data;
using Treasury.Api.Integrations;
using Treasury.Api.Ownership;

namespace Treasury.Api.Imports;

public static class ImportEndpoints
{
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    /// Exactly one of AccountId and MultiCurrencyAccountId is set: what the file was imported into.
    public record SourceFileResponse(Guid Id, Guid? AccountId, Guid? MultiCurrencyAccountId, string FileName, long Size,
        DateTimeOffset ImportedAt, int TransactionCount);

    /// An Account the import created for a new currency of a Multi-currency account (#25).
    public record CreatedAccountResponse(Guid Id, string Name, string Currency)
    {
        public static List<CreatedAccountResponse> From(ImportResult result) =>
            result.CreatedAccounts.Select(a => new CreatedAccountResponse(a.Id, a.Name, a.Currency)).ToList();
    }

    public record ImportResponse(SourceFileResponse SourceFile, int ImportedCount, int SkippedCount,
        IReadOnlyList<string> Warnings, IReadOnlyList<CreatedAccountResponse> CreatedAccounts);

    public static void AddImports(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ImportFiles>();
        builder.Services.AddSingleton<ImportLocks>();
        builder.Services.AddScoped<ImportPipeline>();
        builder.Services.AddScoped<ImportUndo>();
        builder.Services.AddScoped<IImportStep, DuplicateDetection>();
        // Production Integrations (tests register their fake ones on top). Bank-specific ones (#22) go here.
        builder.Services.AddSingleton<IIntegration, GenericCsvIntegration>();
        builder.Services.AddSingleton<IIntegration, TkbIntegration>();
        builder.Services.AddSingleton<IIntegration, RevolutIntegration>();
    }

    /// Upload into an Account (POST /api/accounts/{id}/imports) or a Multi-currency account
    /// (POST /api/multi-currency-accounts/{id}/imports, #25), and the kept Source files.
    /// Source files are visible to whoever can see what they were imported into (own or Family).
    public static void MapImports(this RouteGroupBuilder api)
    {
        // multipart/form-data with one file field "file". Read by hand (no IFormFile binding): no antiforgery
        // metadata (ADR-0006) and our own 400s in the usual ValidationProblem shape.
        api.MapPost("/accounts/{id:guid}/imports", async (Guid id, HttpRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, ImportPipeline pipeline, CancellationToken ct) =>
        {
            var viewer = user.MemberId();
            var account = await db.Accounts.OwnedByViewerOrFamily(viewer).SingleOrDefaultAsync(a => a.Id == id, ct);
            if (account is null) return Results.NotFound();
            if (!account.Owner.IsManageableBy(viewer)) return Results.Forbid();
            // Its statements hold every currency of the group: they are imported into the group (ADR-0009).
            if (account.MultiCurrencyAccountId is { } multiCurrencyAccountId)
            {
                var name = await db.MultiCurrencyAccounts.Where(m => m.Id == multiCurrencyAccountId)
                    .Select(m => m.Name).SingleAsync(ct);
                return FileError($"This Account belongs to the Multi-currency account \"{name}\". Import its " +
                    "statements into the Multi-currency account; each row goes to the Account of its currency.");
            }

            var (upload, error) = await ReadUploadAsync(request, ct);
            if (error is not null) return error;
            return Respond(await pipeline.ImportAsync(account, upload!.WithMember(viewer), ct));
        });

        api.MapPost("/multi-currency-accounts/{id:guid}/imports", async (Guid id, HttpRequest request,
            ClaimsPrincipal user, TreasuryDbContext db, ImportPipeline pipeline, CancellationToken ct) =>
        {
            var viewer = user.MemberId();
            var multiCurrencyAccount = await db.MultiCurrencyAccounts.Include(m => m.Accounts)
                .OwnedByViewerOrFamily(viewer).SingleOrDefaultAsync(m => m.Id == id, ct);
            if (multiCurrencyAccount is null) return Results.NotFound();
            if (!multiCurrencyAccount.Owner.IsManageableBy(viewer)) return Results.Forbid();

            var (upload, error) = await ReadUploadAsync(request, ct);
            if (error is not null) return error;
            return Respond(await pipeline.ImportAsync(multiCurrencyAccount, upload!.WithMember(viewer), ct));
        });

        api.MapGet("/accounts/{id:guid}/source-files", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var account = await db.Accounts.OwnedByViewerOrFamily(user.MemberId()).SingleOrDefaultAsync(a => a.Id == id);
            if (account is null) return Results.NotFound();
            var list = await Responses(db, db.SourceFiles.Where(f => f.AccountId == id)
                .OrderByDescending(f => f.ImportedAt)).ToListAsync();
            return Results.Ok(list);
        });

        api.MapGet("/multi-currency-accounts/{id:guid}/source-files", async (Guid id, ClaimsPrincipal user,
            TreasuryDbContext db) =>
        {
            var multiCurrencyAccount = await db.MultiCurrencyAccounts.OwnedByViewerOrFamily(user.MemberId())
                .SingleOrDefaultAsync(m => m.Id == id);
            if (multiCurrencyAccount is null) return Results.NotFound();
            var list = await Responses(db, db.SourceFiles.Where(f => f.MultiCurrencyAccountId == id)
                .OrderByDescending(f => f.ImportedAt)).ToListAsync();
            return Results.Ok(list);
        });

        api.MapGet("/source-files/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var file = await Responses(db, VisibleSourceFiles(db, user.MemberId()).Where(f => f.Id == id)).SingleOrDefaultAsync();
            return file is null ? Results.NotFound() : Results.Ok(file);
        });

        // The original file, as imported.
        api.MapGet("/source-files/{id:guid}/content", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            ImportFiles files) =>
        {
            var file = await VisibleSourceFiles(db, user.MemberId()).SingleOrDefaultAsync(f => f.Id == id);
            if (file is null) return Results.NotFound();
            var path = files.KeptPath(file.StoredPath);
            return File.Exists(path)
                ? Results.File(path, "application/octet-stream", file.FileName)
                : Results.NotFound();
        });

        // Undo the import (#10): removes the Source file and every Transaction imported from it.
        api.MapDelete("/source-files/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            ImportUndo undo, CancellationToken ct) =>
        {
            var viewer = user.MemberId();
            var file = await VisibleSourceFiles(db, viewer).Include(f => f.Account).Include(f => f.MultiCurrencyAccount)
                .AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);
            if (file is null) return Results.NotFound();
            if (!file.Target.Owner.IsManageableBy(viewer)) return Results.Forbid();
            await undo.UndoAsync(file, ct);
            return Results.NoContent();
        });
    }

    private static IResult Respond(ImportResult result)
    {
        if (!result.Succeeded) return FileError(result.Error!);
        var response = new ImportResponse(ToResponse(result.SourceFile!, result.ImportedCount), result.ImportedCount,
            result.SkippedCount, result.Warnings, CreatedAccountResponse.From(result));
        return Results.Created($"/api/source-files/{result.SourceFile!.Id}", response);
    }

    /// The uploaded "file" field, or the 400 to answer with.
    private static async Task<(IncomingFile? File, IResult? Error)> ReadUploadAsync(HttpRequest request,
        CancellationToken ct)
    {
        IFormFile? file;
        try
        {
            file = request.HasFormContentType ? (await request.ReadFormAsync(ct)).Files.GetFile("file") : null;
        }
        catch (Exception e) when (e is InvalidDataException or BadHttpRequestException)
        {
            return (null, FileError("The upload couldn't be read; choose the file again."));
        }
        if (file is null || file.Length == 0) return (null, FileError("Choose a statement file."));
        if (file.Length > MaxUploadBytes)
            return (null, FileError($"The file is larger than {MaxUploadBytes / 1024 / 1024} MB."));

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (new IncomingFile(file.FileName, buffer.ToArray()), null);
    }

    private static IncomingFile WithMember(this IncomingFile file, Guid memberId) =>
        file with { ImportedByMemberId = memberId };

    private static IResult FileError(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

    /// Source files imported into an Account or Multi-currency account the viewer can see.
    private static IQueryable<SourceFile> VisibleSourceFiles(TreasuryDbContext db, Guid viewer)
    {
        var accounts = db.Accounts.OwnedByViewerOrFamily(viewer).Select(a => (Guid?)a.Id);
        var multiCurrencyAccounts = db.MultiCurrencyAccounts.OwnedByViewerOrFamily(viewer).Select(m => (Guid?)m.Id);
        return db.SourceFiles.Where(f =>
            accounts.Contains(f.AccountId) || multiCurrencyAccounts.Contains(f.MultiCurrencyAccountId));
    }

    private static IQueryable<SourceFileResponse> Responses(TreasuryDbContext db, IQueryable<SourceFile> files) =>
        files.Select(f => new SourceFileResponse(f.Id, f.AccountId, f.MultiCurrencyAccountId, f.FileName, f.Size,
            f.ImportedAt, db.Transactions.Count(t => t.SourceFileId == f.Id)));

    private static SourceFileResponse ToResponse(SourceFile f, int transactionCount) =>
        new(f.Id, f.AccountId, f.MultiCurrencyAccountId, f.FileName, f.Size, f.ImportedAt, transactionCount);
}
