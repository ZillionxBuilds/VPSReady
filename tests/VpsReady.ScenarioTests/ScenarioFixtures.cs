namespace VpsReady.ScenarioTests;

public enum ScenarioFixtureKind
{
    Normal,
    Partial,
    Malformed,
    LocaleVaried,
}

public sealed record ScenarioFixtureDescriptor(
    string Id,
    ScenarioFixtureKind Kind,
    string RelativePath,
    string Purpose);

/// <summary>
/// Golden output conventions for future Ubuntu adapters.  Fixtures are bounded,
/// sanitized and intentionally contain no credentials or server identifiers.
/// Failure timing/permission/disconnect cases are represented by scenario
/// profiles and fault IDs instead of pretending to be command output.
/// </summary>
public static class ScenarioFixtures
{
    public static IReadOnlyList<ScenarioFixtureDescriptor> Catalog { get; } =
    [
        new("ubuntu.overview.normal", ScenarioFixtureKind.Normal, "ubuntu/overview.normal.txt", "Complete supported overview fields"),
        new("ubuntu.overview.partial", ScenarioFixtureKind.Partial, "ubuntu/overview.partial.txt", "One field absent while other fields remain usable"),
        new("ubuntu.overview.malformed", ScenarioFixtureKind.Malformed, "ubuntu/overview.malformed.txt", "Untrusted output that must become Unknown"),
        new("ubuntu.overview.locale-varied", ScenarioFixtureKind.LocaleVaried, "ubuntu/overview.locale-varied.txt", "Predictable locale variation with stable keys"),
        new("scenario.timeout", ScenarioFixtureKind.Normal, "failures/timeout.json", "Finite command timeout"),
        new("scenario.cancellation", ScenarioFixtureKind.Normal, "failures/cancellation.json", "User cancellation"),
        new("scenario.permission-denied", ScenarioFixtureKind.Normal, "failures/permission-denied.json", "Root/sudo or file permission failure"),
        new("scenario.disconnect", ScenarioFixtureKind.Normal, "failures/disconnect.json", "Mid-command transport disconnect"),
    ];

    public static string Load(ScenarioFixtureKind kind)
    {
        var descriptor = Catalog.First(fixture => fixture.Kind == kind && fixture.RelativePath.StartsWith("ubuntu/", StringComparison.Ordinal));
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    public static string LoadText(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Fixture paths must be relative to the approved fixture root.", nameof(relativePath));
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    public static ScenarioFixtureDescriptor Get(string fixtureId) =>
        Catalog.First(fixture => string.Equals(fixture.Id, fixtureId, StringComparison.Ordinal));

    public static bool TryGet(string fixtureId, out ScenarioFixtureDescriptor? descriptor)
    {
        descriptor = Catalog.FirstOrDefault(fixture => string.Equals(fixture.Id, fixtureId, StringComparison.Ordinal));
        return descriptor is not null;
    }
}
