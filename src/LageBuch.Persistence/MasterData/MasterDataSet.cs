using System.Text.Encodings.Web;
using System.Text.Json;
using LageBuch.Domain.Atemschutz;

namespace LageBuch.Persistence.MasterData;

public sealed record Street(string Name, string District);

/// <summary>A named link — Stammdaten entry so useful external resources can be opened from an Einsatz.</summary>
public sealed record Link(string Name, string Url);

/// <summary>One Checkliste template entry — the Stammdaten-editable source an incident's Aufbau/Abbau
/// checklist is seeded from at start.</summary>
public sealed record ChecklistTemplateItem(string Text, bool IsMandatory);

/// <summary>
/// Configurable operational defaults — the timer/duration values the app used to bake in as
/// constants. Stored alongside the master-data lists so an install can tune them once and have
/// every new incident pick them up. Every field is a plain minute/bar count with a sensible
/// <see cref="Defaults"/>, so a fresh or older store (which has never written these) still yields
/// usable values rather than zeros.
/// </summary>
public sealed record IncidentSettings(
    // "Rückmeldung an ILS" — minutes until the first reminder is due.
    int IlsReminderIntervalMinutes,
    // "Rückmeldung an ILS" — recurring interval after the first reminder. Stored/editable
    // here but not yet consumed by the reminder timer (see #70).
    int IlsReminderFollowUpIntervalMinutes,
    // Atemschutz Einsatzzeit for an ordinary AGT-Trupp.
    int AgtMaxDurationMinutes,
    // Atemschutz Einsatzzeit for a CSA-Trupp (chemical suit) — shorter than an AGT.
    int CsaMaxDurationMinutes,
    // Atemschutz Einsatzzeit for an LPA-Trupp (long-duration apparatus) — longer than an AGT.
    int LpaMaxDurationMinutes,
    // Interval between Druckkontrollen (Atemschutzkontrolle).
    int PressureControlIntervalMinutes,
    // Rückzugsdruck: pressure at or below which a Trupp must turn back.
    int ReturnPressureBar)
{
    /// <summary>
    /// The compiled-in fallbacks, kept in step with the domain's Atemschutz constants so there is a
    /// single source of truth for the shared values. Used whenever the store holds no override.
    /// </summary>
    public static IncidentSettings Defaults { get; } = new(
        IlsReminderIntervalMinutes: 15,
        IlsReminderFollowUpIntervalMinutes: 30,
        AgtMaxDurationMinutes: AtemschutzTrupp.DefaultMaxDurationMinutes,
        CsaMaxDurationMinutes: AtemschutzTrupp.DefaultChemicalMaxDurationMinutes,
        LpaMaxDurationMinutes: AtemschutzTrupp.DefaultLpaMaxDurationMinutes,
        PressureControlIntervalMinutes: AtemschutzTrupp.DefaultPressureControlIntervalMinutes,
        ReturnPressureBar: AtemschutzTrupp.DefaultReturnPressureBar);
}

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

/// <summary>
/// One vehicle of a Wache (#76). The Wache reference is the brigade's name as spelled in the
/// Brigades list (a free-text list, so no id exists to point at); the seat count feeds the
/// Stärke preset when the vehicle is picked in the Kräfte entry.
/// </summary>
public sealed record Vehicle(string Wache, string CallSign, int Seats);

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
    IReadOnlyList<Link> Links,
    IReadOnlyList<ChecklistTemplateItem> ChecklistTemplateAufbau,
    IReadOnlyList<ChecklistTemplateItem> ChecklistTemplateAbbau,
    IReadOnlyList<string> TruppTypes,
    IReadOnlyList<Person> Personnel,
    // Einsatzart values (ABek Bayern) — the leading token of the complete Einsatznummer.
    IReadOnlyList<string> Einsatzarten,
    // Vehicles per Wache with their seat count (#76).
    IReadOnlyList<Vehicle> Vehicles,
    // Operational defaults (timers, durations). Unlike the lists, always populated — a store with
    // no overrides yields IncidentSettings.Defaults, never a zeroed record.
    IncidentSettings Settings)
{
    /// <summary>
    /// Every category empty. Intended for tests and for callers that need a starting point to
    /// override with a <c>with</c> expression, so that adding a category to this positional record
    /// does not force an edit in every construction site.
    /// </summary>
    public static MasterDataSet Empty { get; } = new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Street>(),
        Array.Empty<Link>(),
        Array.Empty<ChecklistTemplateItem>(), Array.Empty<ChecklistTemplateItem>(),
        Array.Empty<string>(), Array.Empty<Person>(), Array.Empty<string>(),
        Array.Empty<Vehicle>(),
        IncidentSettings.Defaults);

    /// <summary>
    /// True when no category holds a single entry. A fresh install starts here, and it is the
    /// condition under which the Stammdaten editor offers Import — a bootstrap, not a merge.
    /// <see cref="Settings"/> deliberately does not count: it always carries defaults, and letting it
    /// mark the set non-empty would suppress the Import bootstrap on an otherwise fresh install.
    /// </summary>
    public bool IsEmpty =>
        Roles.Count == 0 && Status.Count == 0 && Equipment.Count == 0 && Districts.Count == 0
        && RadioCallSigns.Count == 0 && Brigades.Count == 0 && UnitStatus.Count == 0
        && Streets.Count == 0 && Links.Count == 0 && ChecklistTemplateAufbau.Count == 0 && ChecklistTemplateAbbau.Count == 0
        && TruppTypes.Count == 0
        && Personnel.Count == 0 && Einsatzarten.Count == 0
        && Vehicles.Count == 0;
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

        IReadOnlyList<Link> links =
            root.TryGetProperty("links", out var lk) && lk.ValueKind == JsonValueKind.Array
                ? lk.EnumerateArray()
                    .Select(l => new Link(l.GetProperty("name").GetString()!, l.GetProperty("url").GetString()!))
                    .ToList()
                : Array.Empty<Link>();

        var (checklistAufbau, checklistAbbau) = ParseChecklistTemplate(root);

        IReadOnlyList<Vehicle> vehicles =
            root.TryGetProperty("vehicles", out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray()
                    .Select(x => new Vehicle(
                        x.GetProperty("wache").GetString()!,
                        x.GetProperty("callSign").GetString()!,
                        x.TryGetProperty("seats", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0))
                    .ToList()
                : Array.Empty<Vehicle>();

        return new MasterDataSet(
            Arr(root, "roles"),
            Arr(root, "status"),
            Arr(root, "equipment"),
            Arr(root, "districts"),
            Arr(root, "radioCallSigns"),
            Arr(root, "brigades"),
            Arr(root, "unitStatus"),
            streets,
            links,
            checklistAufbau,
            checklistAbbau,
            Arr(root, "truppTypes"),
            ParsePersonnel(root),
            Arr(root, "einsatzarten"),
            vehicles,
            ParseSettings(root));
    }

    /// <summary>
    /// Reads the Aufbau/Abbau template lists. A file still on the old flat <c>checklistTemplate</c>
    /// string array (pre-split) maps every item to Aufbau, all optional — the safest default, since
    /// nothing silently becomes a blocking requirement — with Abbau left empty.
    /// </summary>
    private static (IReadOnlyList<ChecklistTemplateItem> Aufbau, IReadOnlyList<ChecklistTemplateItem> Abbau)
        ParseChecklistTemplate(JsonElement root)
    {
        if (root.TryGetProperty("checklistTemplateAufbau", out _) || root.TryGetProperty("checklistTemplateAbbau", out _))
            return (ChecklistItems(root, "checklistTemplateAufbau"), ChecklistItems(root, "checklistTemplateAbbau"));

        if (root.TryGetProperty("checklistTemplate", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
            return (legacy.EnumerateArray().Select(x => new ChecklistTemplateItem(x.GetString()!, false)).ToList(),
                Array.Empty<ChecklistTemplateItem>());

        return (Array.Empty<ChecklistTemplateItem>(), Array.Empty<ChecklistTemplateItem>());

        static IReadOnlyList<ChecklistTemplateItem> ChecklistItems(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray()
                    .Select(x => new ChecklistTemplateItem(
                        x.GetProperty("text").GetString()!,
                        x.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True))
                    .ToList()
                : Array.Empty<ChecklistTemplateItem>();
    }

    /// <summary>
    /// Reads the optional <c>settings</c> object. A missing object, or any missing field within it,
    /// falls back to <see cref="IncidentSettings.Defaults"/> so an older or partial file still yields
    /// a complete record.
    /// </summary>
    private static IncidentSettings ParseSettings(JsonElement root)
    {
        var d = IncidentSettings.Defaults;
        if (!root.TryGetProperty("settings", out var s) || s.ValueKind != JsonValueKind.Object)
            return d;

        static int Int(JsonElement e, string prop, int fallback) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

        return new IncidentSettings(
            Int(s, "ilsReminderIntervalMinutes", d.IlsReminderIntervalMinutes),
            Int(s, "ilsReminderFollowUpIntervalMinutes", d.IlsReminderFollowUpIntervalMinutes),
            Int(s, "agtMaxDurationMinutes", d.AgtMaxDurationMinutes),
            Int(s, "csaMaxDurationMinutes", d.CsaMaxDurationMinutes),
            Int(s, "lpaMaxDurationMinutes", d.LpaMaxDurationMinutes),
            Int(s, "pressureControlIntervalMinutes", d.PressureControlIntervalMinutes),
            Int(s, "returnPressureBar", d.ReturnPressureBar));
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
            einsatzarten = set.Einsatzarten,
            checklistTemplateAufbau = set.ChecklistTemplateAufbau.Select(i => new { text = i.Text, mandatory = i.IsMandatory }),
            checklistTemplateAbbau = set.ChecklistTemplateAbbau.Select(i => new { text = i.Text, mandatory = i.IsMandatory }),
            streets = set.Streets.Select(s => new { name = s.Name, district = s.District }),
            links = set.Links.Select(l => new { name = l.Name, url = l.Url }),
            vehicles = set.Vehicles.Select(v => new { wache = v.Wache, callSign = v.CallSign, seats = v.Seats }),
            personnel = set.Personnel.Select(p => new
            {
                lastName = p.LastName,
                firstName = p.FirstName,
                role = p.Role,
                callSign = p.CallSign,
                phone = p.Phone,
            }),
            settings = new
            {
                ilsReminderIntervalMinutes = set.Settings.IlsReminderIntervalMinutes,
                ilsReminderFollowUpIntervalMinutes = set.Settings.IlsReminderFollowUpIntervalMinutes,
                agtMaxDurationMinutes = set.Settings.AgtMaxDurationMinutes,
                csaMaxDurationMinutes = set.Settings.CsaMaxDurationMinutes,
                lpaMaxDurationMinutes = set.Settings.LpaMaxDurationMinutes,
                pressureControlIntervalMinutes = set.Settings.PressureControlIntervalMinutes,
                returnPressureBar = set.Settings.ReturnPressureBar,
            },
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
