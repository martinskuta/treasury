namespace Treasury.Api.Integrations;

/// A supported source of statements (GLOSSARY: Integration), registered by its Key (e.g. "tkb").
/// The reader part (Source file content in, parsed statement out) is added with the import pipeline (#7).
public interface IIntegration
{
    /// Stable key stored on Accounts, e.g. "tkb", "revolut".
    string Key { get; }

    /// Name shown to Members.
    string Name { get; }

    /// Whether this Integration's statements carry an Account identifier (IBAN, last card digits) that can be
    /// checked against the Account's. Only then can an Account using it have an identifier.
    bool CanProvideIdentifier { get; }
}

/// All registered Integrations by key. Register one with services.AddSingleton&lt;IIntegration, MyIntegration&gt;().
public sealed class IntegrationRegistry(IEnumerable<IIntegration> integrations)
{
    private readonly Dictionary<string, IIntegration> _byKey =
        integrations.ToDictionary(i => i.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<IIntegration> All => _byKey.Values;

    public IIntegration? Find(string? key) => key is not null && _byKey.TryGetValue(key, out var i) ? i : null;
}
