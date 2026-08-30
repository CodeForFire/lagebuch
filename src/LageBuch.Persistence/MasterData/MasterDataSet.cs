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

/// <summary>
/// The single source of truth for fictional example data shown in input-field placeholders
/// (#137) and used to build test/screenshot fixtures. Real personnel and call-sign data must
/// never be compiled into the app (see the privacy note on <see cref="Person"/>), so every value
/// here is invented, following the classic German "Mustermann/Musterfrau" placeholder-name
/// convention already used ad hoc across the test suite. Centralizing it means a placeholder and
/// the fixture that renders it for a PR screenshot can never drift apart.
/// </summary>
public static class AnonymizedExampleData
{
    // Roster (Stammdaten) persona.
    public const string PersonLastName = "Mustermann";
    public const string PersonFirstName = "Max";
    public const string PersonLastNameAlt = "Musterfrau";
    public const string PersonFirstNameAlt = "Erika";
    public const string PhoneNumber = "01 71 / 1 23 45 67";
    public const string PhoneNumberAlt = "01 71 / 7 65 43 21";

    // Ad-hoc persona: whoever is entering data right now (operator, Truppführer/-mann).
    // Deliberately a different fictional surname from the roster persona above, so a screenshot
    // never shows the same invented person as both "the roster entry" and "today's operator".
    public const string OperatorSurname = "Müller";
    public const string OperatorSurnameAlt = "Schmidt";
    public const string OperatorSurnameThird = "Wagner";
    // First name for the full-name example (OperatorPromptView's NAME field). Deliberately not
    // "Thomas" — that would read as a real contributor's actual name rather than a placeholder.
    public const string OperatorFirstName = "Jens";

    // Callsigns / brigades.
    public const string CallSign = "FFB 1/40/1";
    public const string SecondCallSign = "FFB 1/44/1";
    public const string OtherBrigadeCallSign = "Aich 42/1";
    public const string Brigade = "FFB Wache 1";
    public const string SecondBrigade = "Aich";

    // Misc categorical examples used by non-Kräfte views.
    public const string BuildingName = "Haus A";
    public const string FileName = "Lageplan.pdf";
    public const string RoleExample = "GF";
    public const string SectionExample = "Abschnitt 1";
    public const string TimerMinutesExample = "30";
    public const string LinkName = "Wetterdienst";
    public const string LinkUrl = "https://dwd.de";

    // Derived placeholder strings. Compile-time const concatenation, so the "z. B." prefix and
    // the underlying value can never drift apart from one another.
    public const string CallSignPlaceholder = "z. B. " + CallSign;
    public const string SecondCallSignPlaceholder = "z. B. " + SecondCallSign;
    public const string BrigadePlaceholder = "z. B. " + Brigade;
    public const string PersonLastNamePlaceholder = "z. B. " + PersonLastName;
    public const string PersonFirstNamePlaceholder = "z. B. " + PersonFirstName;
    public const string PersonDisplayNamePlaceholder = "z. B. " + PersonLastName + ", " + PersonFirstName;
    public const string PhoneNumberPlaceholder = "z. B. " + PhoneNumber;
    public const string OperatorNamePlaceholder = "z. B. " + OperatorSurname;
    public const string OperatorNamePlaceholderAlt = "z. B. " + OperatorSurnameAlt;
    public const string OperatorNamePlaceholderThird = "z. B. " + OperatorSurnameThird;
    // Full-name form, for the one field that asks for a proper name rather than a short crew/
    // assignee entry (OperatorPromptView's NAME field).
    public const string OperatorFullNamePlaceholder = "z. B. " + OperatorSurname + ", " + OperatorFirstName;
    public const string BuildingNamePlaceholder = "z. B. " + BuildingName;
    public const string FileNamePlaceholder = "z. B. " + FileName;
    public const string RolePlaceholder = "z. B. " + RoleExample;
    public const string SectionPlaceholder = "z. B. " + SectionExample;
    public const string TimerMinutesPlaceholder = "z. B. " + TimerMinutesExample;
    public const string LinkNamePlaceholder = "z. B. " + LinkName;
    public const string LinkUrlPlaceholder = "z. B. " + LinkUrl;

    // A field that is genuinely optional reuses this idiom rather than inventing a second
    // convention for the same idea (see OperatorPromptView's KeywordBox).
    public const string OptionalCallSignPlaceholder = "optional, z. B. " + CallSign;

    // Ready-built collections for fixtures that need a fuller MasterDataSet (render/PR-screenshot
    // tests). Built from the same constants above so a single-value placeholder and a list-based
    // fixture never show contradictory example data.
    public static readonly IReadOnlyList<string> Brigades =
        new[] { Brigade, "FFB Wache 2", SecondBrigade, "Puch", "Emmering" };

    public static readonly IReadOnlyList<string> RadioCallSigns =
        new[] { CallSign, "FFB 1/23/1", OtherBrigadeCallSign, "Land 1" };

    public static readonly IReadOnlyList<Vehicle> Vehicles = new[]
    {
        new Vehicle(Brigade, CallSign, 9),
        new Vehicle(Brigade, SecondCallSign, 6),
        new Vehicle(SecondBrigade, OtherBrigadeCallSign, 6),
    };

    public static readonly IReadOnlyList<Person> Personnel = new[]
    {
        new Person(PersonLastName, PersonFirstName, "ZF", "Land 1", PhoneNumber),
        new Person(PersonLastNameAlt, PersonFirstNameAlt, "GF", null, PhoneNumberAlt),
    };

    public static readonly IReadOnlyList<Link> Links = new[]
    {
        new Link(LinkName, LinkUrl),
        new Link("Kartendienst", "https://example.org/karte"),
    };
}

/// <summary>
/// The operator's configured region of operation (#150, Plan B) — a folder expected to hold
/// <c>region.mbtiles</c> (map tiles) and <c>region.dem</c> (elevation), set up once at
/// installation. Global config, like everything else in <see cref="MasterDataSet"/>.
/// </summary>
public sealed record Einsatzgebiet(string Name, string FolderPath)
{
    public static Einsatzgebiet Empty { get; } = new(string.Empty, string.Empty);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(FolderPath);
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
    IncidentSettings Settings,
    // Region of operation for the Wasserförderung map (#150 phase 2). Unlike the lists, always
    // populated — a store with no override yields Einsatzgebiet.Empty, never a null.
    Einsatzgebiet Einsatzgebiet)
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
        IncidentSettings.Defaults,
        Einsatzgebiet.Empty);

    /// <summary>
    /// True when no category holds a single entry. A fresh install starts here, and it is the
    /// condition under which the Stammdaten editor offers Import — a bootstrap, not a merge.
    /// <see cref="Settings"/> and the Einsatzgebiet field deliberately do not count: they always
    /// carry a value (defaults, or an empty region), and letting either mark the set non-empty
    /// would suppress the Import bootstrap on an otherwise fresh install.
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
            ParseSettings(root),
            ParseEinsatzgebiet(root));
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

    /// <summary>
    /// Reads the optional <c>einsatzgebiet</c> object. A missing object falls back to
    /// <see cref="Einsatzgebiet.Empty"/> so an older file still yields a complete record.
    /// </summary>
    private static Einsatzgebiet ParseEinsatzgebiet(JsonElement root)
    {
        if (!root.TryGetProperty("einsatzgebiet", out var e) || e.ValueKind != JsonValueKind.Object)
            return Einsatzgebiet.Empty;

        return new Einsatzgebiet(
            e.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            e.TryGetProperty("folderPath", out var f) ? f.GetString() ?? string.Empty : string.Empty);
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
            einsatzgebiet = new { name = set.Einsatzgebiet.Name, folderPath = set.Einsatzgebiet.FolderPath },
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
