using System.Globalization;
using System.Text;
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
}
