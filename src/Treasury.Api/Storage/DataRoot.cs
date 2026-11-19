namespace Treasury.Api.Storage;

/// The folder holding Treasury's files (Storage:DataRoot, env Storage__DataRoot): Ingest folders under
/// ingest/ and, later, kept Source files under source-files/. The AppHost sets it (run mode: repo /data,
/// publish: /data in the container, bind-mounted from TREASURY_DATA_DIR). Tests use a temp folder per test.
public sealed class DataRoot
{
    public DataRoot(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        IngestRoot = System.IO.Path.Combine(Path, "ingest");
        SourceFilesRoot = System.IO.Path.Combine(Path, "source-files");
    }

    public string Path { get; }

    /// Parent of every Account's Ingest folder: ingest/&lt;account-slug&gt;/ (flat, ADR-0002).
    public string IngestRoot { get; }

    /// Where imported Source files will be kept (#7).
    public string SourceFilesRoot { get; }
}

public static class DataRootSetup
{
    public static void AddDataRoot(this WebApplicationBuilder builder) =>
        // Resolved lazily (and eagerly right after Build in Program.cs), so EF design-time tools that stop at
        // Build() don't need the setting.
        builder.Services.AddSingleton(sp =>
        {
            var path = sp.GetRequiredService<IConfiguration>()["Storage:DataRoot"];
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    "Storage:DataRoot (env Storage__DataRoot) is not set; the AppHost normally sets it.");
            var root = new DataRoot(path);
            Directory.CreateDirectory(root.IngestRoot);
            return root;
        });
}
