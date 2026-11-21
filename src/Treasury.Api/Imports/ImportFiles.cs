using Treasury.Api.Accounts;
using Treasury.Api.Integrations;
using Treasury.Api.Storage;

namespace Treasury.Api.Imports;

/// The files around an import:
/// - the kept copy of every Source file, under source-files/&lt;slug&gt;/&lt;source-file-id&gt;/&lt;name&gt; (the slug of
///   the Account or Multi-currency account it was imported into, see <see cref="IImportTarget"/>)
///   (owned by Treasury; downloads and undo use it, whatever Members do in the Ingest folder);
/// - the file placed in the Ingest folder it was imported from (or would have been dropped into): processed/&lt;name&gt; after a successful import, or
///   failed/&lt;name&gt; plus failed/&lt;name&gt;.error.txt with the reason. An existing name gets " (2)", " (3)"...
public sealed class ImportFiles(DataRoot dataRoot, IngestFolders folders, TimeProvider clock)
{
    public const string ErrorNoteSuffix = ".error.txt";

    /// Writes the kept copy and returns its path relative to source-files/ (stored on the SourceFile).
    public string Keep(IImportTarget target, Guid sourceFileId, SourceFileContent content)
    {
        var relative = Path.Combine(target.Slug, sourceFileId.ToString("N"), content.FileName);
        var full = KeptPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content.Bytes);
        return relative.Replace('\\', '/');
    }

    /// Full path of a kept copy from its stored relative path.
    public string KeptPath(string storedPath) => Path.Combine(dataRoot.SourceFilesRoot, storedPath);

    public void DeleteKept(string storedPath)
    {
        var full = KeptPath(storedPath);
        if (File.Exists(full)) File.Delete(full);
        var folder = Path.GetDirectoryName(full)!;
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    /// Puts the file into processed/ and returns where it ended up.
    public string PlaceProcessed(IImportTarget target, SourceFileContent content, string? droppedAt) =>
        Place(target, IngestFolders.Processed, content, droppedAt);

    /// Puts the file into failed/ with an error note next to it and returns where the file ended up.
    public string PlaceFailed(IImportTarget target, SourceFileContent content, string? droppedAt, string reason)
    {
        var placed = Place(target, IngestFolders.Failed, content, droppedAt);
        File.WriteAllText(placed + ErrorNoteSuffix,
            $"Treasury could not import {Path.GetFileName(placed)} into {target.Description}.{Environment.NewLine}" +
            $"{Environment.NewLine}{reason}{Environment.NewLine}{Environment.NewLine}" +
            $"Nothing was imported. Fix the file and drop it into this Ingest folder again, or drop it into the " +
            $"Ingest folder it belongs to.{Environment.NewLine}Time: {clock.GetUtcNow():yyyy-MM-dd HH:mm:ss} UTC{Environment.NewLine}");
        return placed;
    }

    private string Place(IImportTarget target, string subfolder, SourceFileContent content, string? droppedAt)
    {
        var folder = Path.Combine(folders.PathFor(target.Slug), subfolder);
        Directory.CreateDirectory(folder);
        var placed = FreeName(folder, content.FileName);
        if (droppedAt is not null && File.Exists(droppedAt)) File.Move(droppedAt, placed);
        else File.WriteAllBytes(placed, content.Bytes);
        return placed;
    }

    /// folder/name, or "name (2).ext", "name (3).ext"... when taken (also by an error note).
    private static string FreeName(string folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(folder, fileName);
        for (var n = 2; File.Exists(candidate) || File.Exists(candidate + ErrorNoteSuffix); n++)
            candidate = Path.Combine(folder, $"{stem} ({n}){extension}");
        return candidate;
    }

    /// A file name safe to use in our folders: no folder parts, no invalid characters, at most 255 characters.
    public static string SafeFileName(string? name)
    {
        var fileName = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/')[^1]).Trim();
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']).ToHashSet();
        fileName = new string(fileName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (fileName.Length > 200)
        {
            var extension = Path.GetExtension(fileName);
            fileName = fileName[..(200 - Math.Min(extension.Length, 20))] + extension[..Math.Min(extension.Length, 20)];
        }
        return fileName.Length == 0 ? "statement" : fileName;
    }
}
