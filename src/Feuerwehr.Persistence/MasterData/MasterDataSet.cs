using System.Text.Json;

namespace Feuerwehr.Persistence.MasterData;

public sealed record Street(string Name, string District);

/// <summary>
/// A person from the local roster. Sourced from the gitignored personnel.json, so this list is
/// empty on a fresh clone and on CI — every consumer must treat that as normal rather than as a
/// configuration error, and must still accept a freely typed name.
/// </summary>
public sealed record Person(string LastName, string FirstName, string? Role, string? CallSign, string? Phone)
{
    /// <summary>How the person is offered in pickers and stored on an assignment.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FirstName) ? LastName : $"{LastName}, {FirstName}";
}

public sealed record MasterDataSet(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Status,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Districts,
    IReadOnlyList<string> RadioCallSigns,
    IReadOnlyList<string> Brigades,
    // UnitStatus is the status of a single unit (Alarmiert, Auf Anfahrt, ...) and is deliberately
    // separate from Status above, which is the incident-level vocabulary (aufgenommen, ...).
    IReadOnlyList<string> UnitStatus,
    IReadOnlyList<Street> Streets,
    IReadOnlyList<string> ChecklistTemplate,
    IReadOnlyList<string> TruppTypes,
    IReadOnlyList<Person> Personnel)
{
    /// <summary>
    /// Every category empty. Intended for tests and for callers that need a starting point to
    /// override with a <c>with</c> expression, so that adding a category to this positional record
    /// does not force an edit in every construction site.
    /// </summary>
    public static MasterDataSet Empty { get; } = new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Street>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Person>());
}

public static class MasterDataDefaults
{
    public static MasterDataSet LoadEmbedded()
    {
        using var doc = JsonDocument.Parse(OpenRequired("master-data.json"));
        var root = doc.RootElement;

        static IReadOnlyList<string> Arr(JsonElement e, string prop) =>
            e.GetProperty(prop).EnumerateArray().Select(x => x.GetString()!).ToList();

        var streets = root.GetProperty("streets").EnumerateArray()
            .Select(s => new Street(s.GetProperty("name").GetString()!, s.GetProperty("district").GetString() ?? string.Empty))
            .ToList();

        return new MasterDataSet(
            Arr(root, "roles"),
            Arr(root, "status"),
            Arr(root, "equipment"),
            Arr(root, "districts"),
            Arr(root, "radioCallSigns"),
            Arr(root, "brigades"),
            Arr(root, "unitStatus"),
            streets,
            Arr(root, "checklistTemplate"),
            Arr(root, "truppTypes"),
            LoadPersonnel());
    }

    /// <summary>
    /// Reads the optional personnel roster. It lives in a separate, gitignored file because it is
    /// the only PII in the seed, and it is only embedded when a local export exists — so an absent
    /// resource is the expected state on CI and on a fresh clone, not a failure.
    /// </summary>
    private static IReadOnlyList<Person> LoadPersonnel()
    {
        using var stream = Open("personnel.json");
        if (stream is null)
            return Array.Empty<Person>();

        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("personnel").EnumerateArray()
            .Select(p => new Person(
                p.GetProperty("lastName").GetString()!,
                p.GetProperty("firstName").GetString() ?? string.Empty,
                Opt(p, "role"),
                Opt(p, "callSign"),
                Opt(p, "phone")))
            .ToList();

        static string? Opt(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind is not JsonValueKind.Null ? v.GetString() : null;
    }

    private static Stream OpenRequired(string fileName) =>
        Open(fileName) ?? throw new InvalidOperationException($"Embedded {fileName} not found.");

    private static Stream? Open(string fileName)
    {
        var asm = typeof(MasterDataDefaults).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal));
        return resourceName is null ? null : asm.GetManifestResourceStream(resourceName);
    }
}
