using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Ownership;
using Treasury.Api.Storage;

namespace Treasury.Api.Accounts;

/// The per-Account Ingest folders under the data root (ADR-0002): ingest/&lt;slug&gt;/ with processed/ and failed/.
public sealed class IngestFolders(DataRoot dataRoot)
{
    public const string Processed = "processed";
    public const string Failed = "failed";

    public string PathFor(string slug) => Path.Combine(dataRoot.IngestRoot, slug);

    public bool Exists(string slug) => Directory.Exists(PathFor(slug));

    /// Creates (or completes) the Ingest folder of an Account.
    public void Create(string slug)
    {
        var folder = PathFor(slug);
        Directory.CreateDirectory(Path.Combine(folder, Processed));
        Directory.CreateDirectory(Path.Combine(folder, Failed));
    }

    /// A free slug for a new Account or Multi-currency account called <paramref name="name"/>: its
    /// <see cref="Slugify"/>d name, or that with -2, -3... if an Account, a Multi-currency account, a leftover folder
    /// or one of the <paramref name="reserved"/> slugs (picked earlier for the same save) already uses it.
    /// Two saves may still pick the same slug at the same time; the unique index on Slug rejects the second.
    public async Task<string> UniqueSlugAsync(TreasuryDbContext db, string name, IEnumerable<string>? reserved = null,
        CancellationToken ct = default)
    {
        var slug = Slugify(name);
        var prefix = slug + "-";
        var taken = (await db.Accounts.Select(a => a.Slug).Concat(db.MultiCurrencyAccounts.Select(g => g.Slug))
            .Where(s => s == slug || s.StartsWith(prefix)).ToListAsync(ct)).ToHashSet();
        taken.UnionWith(reserved ?? []);
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate) || Exists(candidate); n++) candidate = $"{slug}-{n}";
        return candidate;
    }

    /// A folder-safe slug from an Account name: lower-case ASCII letters and digits joined by '-'
    /// ("Revolut EUR (Anna)" -> "revolut-eur-anna", "Účet" -> "ucet"). Never empty.
    public static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "account" : slug;
    }
}

/// What files are imported into, with an Ingest folder named after its <see cref="Slug"/>: an Account, or a
/// Multi-currency account (ADR-0009). Its kept Source files are stored under the same slug.
public interface IImportTarget
{
    Guid Id { get; }

    string Name { get; }

    string Slug { get; }

    Owner Owner { get; }

    /// Key of the Integration that reads its statements.
    string IntegrationKey { get; }

    /// The Identifiers (e.g. IBANs) its statements are checked against, and that imports add to (ADR-0002).
    List<string> Identifiers { get; set; }

    /// What it is, for messages to Members: the Account "…" or the Multi-currency account "…".
    string Description { get; }
}
