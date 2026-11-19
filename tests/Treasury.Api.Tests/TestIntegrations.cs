using Treasury.Api.Integrations;

namespace Treasury.Api.Tests;

/// Fake Integrations behind the API seam (no real bank formats in API tests).
public sealed record TestIntegration(string Key, string Name, bool CanProvideIdentifier) : IIntegration;

public static class TestIntegrations
{
    /// Its format carries an Account identifier, so Accounts using it may have one.
    public static readonly TestIntegration WithIdentifier = new("test", "Test bank", CanProvideIdentifier: true);

    /// Its format has no identifier.
    public static readonly TestIntegration WithoutIdentifier = new("test-noid", "Test bank without identifier", CanProvideIdentifier: false);

    public static IReadOnlyList<TestIntegration> All => [WithIdentifier, WithoutIdentifier];
}
