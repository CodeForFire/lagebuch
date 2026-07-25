using System.Text.Encodings.Web;
using System.Text.Json;

namespace Feuerwehr.Persistence.MasterData;

public sealed record Street(string Name, string District);

/// <summary>
/// A person from the local roster. Personal data (names, mobile numbers) that must never be
/// compiled into the app, so it only ever reaches a running install through an explicit import.
/// The roster is empty until then — every consumer must treat that as normal rather than as a
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

    /// <summary>
    /// True when no category holds a single entry. A fresh install starts here, and it is the
    /// condition under which the Stammdaten editor offers Import — a bootstrap, not a merge.
    /// </summary>
    public bool IsEmpty =>
        Roles.Count == 0 && Status.Count == 0 && Equipment.Count == 0 && Districts.Count == 0
        && RadioCallSigns.Count == 0 && Brigades.Count == 0 && UnitStatus.Count == 0
        && Streets.Count == 0 && ChecklistTemplate.Count == 0 && TruppTypes.Count == 0
        && Personnel.Count == 0;
}

/// <summary>
/// Reads and writes the master-data interchange format — one JSON object whose top-level keys are
/// all optional (a missing key means an empty category). The same shape covers the whole set, so a
/// file holding only <c>personnel</c>, only the non-personal lists, or everything at once all parse.
/// This is the format the Stammdaten editor's Import/Export use; nothing is embedded in the app.
/// </summary>
public static class MasterDataJson
{
    public static MasterDataSet Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        static IReadOnlyList<string> Arr(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(x => x.GetString()!).ToList()
                : Array.Empty<string>();

        IReadOnlyList<Street> streets =
            root.TryGetProperty("streets", out var st) && st.ValueKind == JsonValueKind.Array
                ? st.EnumerateArray()
                    .Select(s => new Street(
                        s.GetProperty("name").GetString()!,
                        s.TryGetProperty("district", out var d) ? d.GetString() ?? string.Empty : string.Empty))
                    .ToList()
                : Array.Empty<Street>();

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
            ParsePersonnel(root));
    }

    private static IReadOnlyList<Person> ParsePersonnel(JsonElement root)
    {
        if (!root.TryGetProperty("personnel", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<Person>();

        return arr.EnumerateArray()
            .Select(p => new Person(
                p.GetProperty("lastName").GetString()!,
                p.TryGetProperty("firstName", out var f) ? f.GetString() ?? string.Empty : string.Empty,
                Opt(p, "role"),
                Opt(p, "callSign"),
                Opt(p, "phone")))
            .ToList();

        static string? Opt(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind is not JsonValueKind.Null ? v.GetString() : null;
    }

    /// <summary>
    /// Serializes the whole set in the superset schema, so a file written here re-parses identically.
    /// Indented and with relaxed escaping so umlauts and slashes stay readable in a hand-edited file.
    /// </summary>
    public static string Serialize(MasterDataSet set)
    {
        var model = new
        {
            roles = set.Roles,
            status = set.Status,
            unitStatus = set.UnitStatus,
            equipment = set.Equipment,
            districts = set.Districts,
            radioCallSigns = set.RadioCallSigns,
            brigades = set.Brigades,
            truppTypes = set.TruppTypes,
            checklistTemplate = set.ChecklistTemplate,
            streets = set.Streets.Select(s => new { name = s.Name, district = s.District }),
            personnel = set.Personnel.Select(p => new
            {
                lastName = p.LastName,
                firstName = p.FirstName,
                role = p.Role,
                callSign = p.CallSign,
                phone = p.Phone,
            }),
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
