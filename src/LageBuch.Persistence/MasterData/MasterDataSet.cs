using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;

namespace LageBuch.Persistence.MasterData;

public sealed record MasterDataSet(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> UnitStatus,
    IReadOnlyList<Link> Links,

    // 0..n user-defined Checkliste templates, in the order the Stammdaten editor lists them.
    IReadOnlyList<ChecklistTemplate> ChecklistTemplates,

    // The nav rail's order and per-entry visibility. Empty means "this build's default" — see
    // NavLayout.Default for why that is not materialized here.
    IReadOnlyList<NavEntry> Navigation,

    // Trupp-Typen with the crew size and Einsatzzeit each one calls for (#398). Was a bare list
    // of names until the rules keyed off those names by string comparison.
    IReadOnlyList<TruppType> TruppTypes,
    IReadOnlyList<Person> Personnel,

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
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<Link>(),
        Array.Empty<ChecklistTemplate>(),
        Array.Empty<NavEntry>(),
        Array.Empty<TruppType>(),
        Array.Empty<Person>(),
        Array.Empty<Vehicle>(),
        IncidentSettings.Defaults);

    /// <summary>
    /// True when no category holds a single entry. A fresh install starts here, and it is the
    /// condition under which the Stammdaten editor offers Import — a bootstrap, not a merge.
    /// <see cref="Settings"/> deliberately does not count: it always carries defaults, and letting it
    /// mark the set non-empty would suppress the Import bootstrap on an otherwise fresh install.
    /// <see cref="Navigation"/> is excluded for exactly the same reason — a layout saved once would
    /// otherwise permanently suppress the bootstrap.
    /// </summary>
    public bool IsEmpty =>
        Roles.Count == 0
        && UnitStatus.Count == 0
        && Links.Count == 0 && ChecklistTemplates.Count == 0
        && TruppTypes.Count == 0
        && Personnel.Count == 0
        && Vehicles.Count == 0;

    /// <summary>
    /// The Wachen: the distinct Wache of every vehicle, trimmed, case-insensitively de-duplicated,
    /// in first-seen order. Derived rather than stored, so maintaining the Fahrzeuge alone is
    /// sufficient and a Wache can never go missing from a list while its vehicles still exist.
    /// Recomputed on every access (deliberately not cached: a <c>with</c> copy would carry a stale
    /// cache, and the lists are tiny).
    /// </summary>
    public IReadOnlyList<string> Brigades =>
        Vehicles.Select(v => v.Wache.Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The Funkrufnamen offered as suggestions: every vehicle's callsign followed by every roster
    /// person's callsign, trimmed, case-insensitively de-duplicated, in first-seen order. Derived
    /// like <see cref="Brigades"/>. Callsigns with no home in Stammdaten (a Leitstelle, a
    /// mutual-aid unit) are still accepted as free text wherever a callsign is entered.
    /// </summary>
    public IReadOnlyList<string> RadioCallSigns =>
        Vehicles.Select(v => v.CallSign)
            .Concat(Personnel.Select(p => p.CallSign ?? string.Empty))
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>A named link — Stammdaten entry so useful external resources can be opened from an Einsatz.</summary>
[SuppressMessage("Design", "CA1054", Justification = "Link URLs are free-form display data in persisted master data; System.Uri would make non-parseable values (relay or relative links) fail to load.")]
[SuppressMessage("Design", "CA1056", Justification = "Link URLs are free-form display data in persisted master data; System.Uri would make non-parseable values (relay or relative links) fail to load.")]
public sealed record Link(string Name, string Url);

/// <summary>One Checkliste template entry — the Stammdaten-editable source an incident's
/// Checklisten are seeded from at start.</summary>
public sealed record ChecklistTemplateItem(string Text, bool IsMandatory);

/// <summary>
/// One user-defined Checkliste template: a name, its items, and the id every Einsatz seeded from
/// it carries — which is what lets the Navigation layout name this list, and what keeps an Einsatz
/// matched to it after a rename.
/// </summary>
/// <param name="Id">Stable id. The two that predate user-defined lists are frozen in <see cref="ChecklistDefaults"/>.</param>
/// <param name="Title">The list's name, bare ("Aufbau", not "Checkliste Aufbau").</param>
/// <param name="Items">The template's items, in order.</param>
public sealed record ChecklistTemplate(Guid Id, string Title, IReadOnlyList<ChecklistTemplateItem> Items)
{
    /// <summary>
    /// The two lists as Stammdaten held them before this was configurable, dropping either if it
    /// has no items — the same rule the V23 file migration follows, so an imported legacy file and
    /// a migrated Einsatzdatei agree about which lists exist.
    /// </summary>
    public static IReadOnlyList<ChecklistTemplate> AufbauAbbau(
        IReadOnlyList<ChecklistTemplateItem>? aufbau,
        IReadOnlyList<ChecklistTemplateItem>? abbau)
    {
        var templates = new List<ChecklistTemplate>(2);
        if (aufbau is { Count: > 0 })
        {
            templates.Add(new ChecklistTemplate(
                ChecklistDefaults.AufbauListId, ChecklistDefaults.AufbauTitle, aufbau));
        }

        if (abbau is { Count: > 0 })
        {
            templates.Add(new ChecklistTemplate(
                ChecklistDefaults.AbbauListId, ChecklistDefaults.AbbauTitle, abbau));
        }

        return templates;
    }
}

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

    // Rückzugsdruck: pressure at or below which a Trupp must turn back. The Einsatzzeiten used to
    // sit beside it, one per hard-coded Trupp-Typ name; they are per-Trupp-Typ Stammdaten now (#398).
    int ReturnPressureBar)
{
    /// <summary>
    /// The compiled-in fallbacks, kept in step with the domain's Atemschutz constants so there is a
    /// single source of truth for the shared values. Used whenever the store holds no override.
    /// </summary>
    public static IncidentSettings Defaults { get; } = new(
        IlsReminderIntervalMinutes: 15,
        IlsReminderFollowUpIntervalMinutes: 30,
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
/// One vehicle of a Wache (#76). The Wache is the brigade's name as free text — the set's Wachen
/// (<see cref="MasterDataSet.Brigades"/>) and Funkrufnamen (<see cref="MasterDataSet.RadioCallSigns"/>)
/// are derived from these rows, so the vehicle list is the single place to maintain them. The
/// seat count feeds the Stärke preset when the vehicle is picked in the Kräfte entry. HasZugfuehrer marks a
/// command vehicle (ELW/KdoW) that carries the Zugführer -- unlike Officer/Mannschaft, ZF is
/// not seat-derived, since only specific vehicles carry one. Defaulted so existing call sites
/// and older Stammdaten payloads keep working unchanged.
/// </summary>
public sealed record Vehicle(string Wache, string CallSign, int Seats, bool HasZugfuehrer = false);

/// <summary>
/// One Trupp-Typ: its name, how many people it is crewed by, and the Einsatzzeit it defaults to.
/// <para>
/// Crew size and Einsatzzeit used to be decided by comparing the name against the compiled-in
/// literals "CSA-Trupp" and "LPA-Trupp" (#398). Because the name is Stammdaten the user edits
/// freely, a brigade writing "CSA Trupp" or "Chemietrupp" got a two-person CSA-Trupp accepted
/// without complaint. Carrying the numbers on the row is what makes that impossible.
/// </para>
/// <para>
/// <see cref="MemberCount"/> is expected to be between <see cref="AtemschutzTrupp.StandardMemberCount"/>
/// and <see cref="AtemschutzTrupp.MaxMemberCount"/>, but this record deliberately does <b>not</b>
/// throw on a value outside that range: it is built while parsing a file that crossed a trust
/// boundary, and <c>HomeViewModel</c> catches only the JSON exception types around that parse --
/// an ArgumentException escaping it would leak an open sync connection. Every entrance clamps
/// instead; see <see cref="MasterDataJson"/> and the Stammdaten editor.
/// </para>
/// </summary>
public sealed record TruppType(string Name, int MemberCount, int MaxDurationMinutes)
{
    /// <summary>An ordinary two-person Trupp on the standard Einsatzzeit -- the shape every type
    /// has until someone says otherwise. Keeps the many construction sites to just a name.</summary>
    public TruppType(string name)
        : this(name, AtemschutzTrupp.StandardMemberCount, AtemschutzTrupp.DefaultMaxDurationMinutes)
    {
    }

    /// <summary>
    /// <paramref name="memberCount"/> forced into the range a Trupp can actually have. Used at
    /// every entrance (file import, the editor) so an impossible crew size never reaches the app,
    /// and never throws -- see the class remarks.
    /// </summary>
    public static int ClampMemberCount(int memberCount) => Math.Clamp(
        memberCount, AtemschutzTrupp.StandardMemberCount, AtemschutzTrupp.MaxMemberCount);

    /// <summary>
    /// <paramref name="minutes"/> forced to something a countdown can actually run. Applied at the
    /// same entrances as <see cref="ClampMemberCount"/> and for the same reason: a zero or negative
    /// Einsatzzeit out of a hand-edited store would reach <c>AtemschutzTrupp.Register</c>, whose
    /// <c>ThrowIfNegativeOrZero</c> would throw out of a fire-and-forget mutation and take the UI
    /// with it -- the crash shape #217 already fixed once for the Truppnummer field.
    /// <para>
    /// A floor only. Unlike a crew size this has no structural ceiling: <c>TruppRole</c> fixes how
    /// many people fit on the monitoring sheet, but nothing fixes how long a set of apparatus
    /// lasts, and a brigade running a four-hour LPA must not find that silently shortened here.
    /// The editor's own <c>Maximum</c> is a convenience for the spinner, not a rule about Atemschutz.
    /// </para>
    /// </summary>
    public static int ClampMaxDurationMinutes(int minutes) => Math.Max(1, minutes);
}

/// <summary>
/// The crew size and Einsatzzeit the two named Trupp-Typen carried before #398 moved those numbers
/// onto the Stammdaten row. This is the <b>only</b> place in the codebase where a Trupp-Typ name is
/// compared against a literal, and it is reached only while translating data written by an older
/// version: the SQLite widening in <see cref="MasterDataStore"/>, which runs once per store, and
/// the bare-string branch of <see cref="MasterDataJson"/>, which sees only pre-#398 exports.
/// <para>
/// Both entrances share it so a brigade restoring from a JSON backup and one opening its existing
/// masterdata.db end up with identical rows. It must never gain a third entry -- a new Trupp-Typ
/// with special needs is something the user configures, which is the entire point of the issue.
/// </para>
/// </summary>
/// <summary>
/// The Einsatzzeiten a pre-#398 store configured, one per hard-coded Trupp-Typ name. They lived in
/// <c>md_settings</c> / the JSON <c>settings</c> object and were edited in Stammdaten -> Einstellungen,
/// so they are the brigade's own numbers, not constants -- which is exactly why the migration has to
/// read them instead of assuming the shipped defaults.
/// </summary>
/// <param name="Agt">Einsatzzeit for an ordinary Trupp (<c>agtMaxDurationMinutes</c>).</param>
/// <param name="Chemical">Einsatzzeit for the CSA-Trupp (<c>csaMaxDurationMinutes</c>).</param>
/// <param name="Lpa">Einsatzzeit for the LPA-Trupp (<c>lpaMaxDurationMinutes</c>).</param>
internal readonly record struct LegacyEinsatzzeiten(int Agt, int Chemical, int Lpa)
{
    /// <summary>What the retired <c>IncidentSettings</c> defaulted to, for a store that never
    /// overrode them.</summary>
    public static LegacyEinsatzzeiten Defaults { get; } = new(
        AtemschutzTrupp.DefaultMaxDurationMinutes, ChemicalDefaultMinutes, LpaDefaultMinutes);

    /// <summary>The old <c>IncidentSettings.CsaMaxDurationMinutes</c> default.</summary>
    public const int ChemicalDefaultMinutes = 20;

    /// <summary>The old <c>IncidentSettings.LpaMaxDurationMinutes</c> default.</summary>
    public const int LpaDefaultMinutes = 60;
}

/// <summary>
/// Translates a pre-#398 Trupp-Typ -- a bare name, with its crew size and Einsatzzeit still decided
/// by comparing that name against a compiled-in literal -- into a row that carries both itself.
/// This is the <b>only</b> place in the codebase where a Trupp-Typ name is compared against a
/// literal, and it is reached only while reading data written by an older version: the SQLite
/// widening in <see cref="MasterDataStore"/>, which runs once per store, and the bare-string branch
/// of <see cref="MasterDataJson"/>, which sees only pre-#398 exports.
/// <para>
/// Both entrances share it so a brigade restoring from a JSON backup and one opening its existing
/// masterdata.db end up with identical rows. It must never gain a third name -- a new Trupp-Typ with
/// special needs is something the user configures, which is the entire point of the issue.
/// </para>
/// </summary>
internal static class LegacyTruppTypeDefaults
{
    /// <summary>The designation whose crew size the old rule raised to three.</summary>
    public const string ChemicalName = "CSA-Trupp";

    /// <summary>The designation that only ever differed in its Einsatzzeit.</summary>
    public const string LpaName = "LPA-Trupp";

    /// <summary>
    /// The row a bare pre-#398 name becomes, given the Einsatzzeiten that store actually had.
    /// Trimmed and case-insensitive, matching exactly what the old <c>IsChemicalTrupp</c> /
    /// <c>IsLpaTrupp</c> accepted, so a store migrates with the rule it really ran -- no more and
    /// no less.
    /// </summary>
    public static TruppType ToTruppType(string name, LegacyEinsatzzeiten einsatzzeiten)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (Matches(trimmed, ChemicalName))
        {
            return new TruppType(trimmed, AtemschutzTrupp.MaxMemberCount, einsatzzeiten.Chemical);
        }

        return Matches(trimmed, LpaName)
            ? new TruppType(trimmed, AtemschutzTrupp.StandardMemberCount, einsatzzeiten.Lpa)
            : new TruppType(trimmed, AtemschutzTrupp.StandardMemberCount, einsatzzeiten.Agt);
    }

    private static bool Matches(string trimmed, string legacyName) =>
        string.Equals(trimmed, legacyName, StringComparison.OrdinalIgnoreCase);
}

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
    // convention for the same idea.
    public const string OptionalCallSignPlaceholder = "optional, z. B. " + CallSign;

    // Ready-built collections for fixtures that need a fuller MasterDataSet (render/PR-screenshot
    // tests). Built from the same constants above so a single-value placeholder and a list-based
    // fixture never show contradictory example data. The Wachen and Funkrufnamen a fixture sees
    // are derived from these vehicles (plus the roster's callsigns), as in the app.
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
/// Reads and writes the master-data interchange format — one JSON object whose top-level keys are
/// all optional (a missing key means an empty category). The same shape covers the whole set, so a
/// file holding only <c>personnel</c>, only the non-personal lists, or everything at once all parse.
/// This is the format the Stammdaten editor's Import/Export use; nothing is embedded in the app.
/// </summary>
public static class MasterDataJson
{
    // Keys older exports carried while Wachen and Funkrufnamen were still their own lists. Both
    // are derived from vehicles/personnel now; Parse ignores them, ParseForImport reports what
    // would be lost so the user can add a vehicle for it before saving.
    private const string LegacyBrigadesKey = "brigades";
    private const string LegacyCallSignsKey = "radioCallSigns";

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static MasterDataSet Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseRoot(doc.RootElement);
    }

    /// <summary>
    /// String overload of <see cref="Parse(Stream)"/> — identical format and identical failure
    /// modes. Exists for callers that already hold the JSON as text rather than a stream: the sync
    /// client receives the host's Stammdaten over HTTP as a string (#183).
    /// </summary>
    public static MasterDataSet Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseRoot(doc.RootElement);
    }

    /// <summary>
    /// <see cref="Parse(Stream)"/> for the editor's Import: additionally lists the entries of the
    /// legacy <c>brigades</c> / <c>radioCallSigns</c> keys that no vehicle or roster person in the
    /// same file covers — those are not imported, and the editor shows them so nothing vanishes
    /// silently. A current-format file yields an empty list.
    /// </summary>
    public static MasterDataImportResult ParseForImport(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var set = ParseRoot(doc.RootElement);
        var dropped = Arr(doc.RootElement, LegacyBrigadesKey)
            .Where(b => !set.Brigades.Contains(b.Trim(), StringComparer.OrdinalIgnoreCase))
            .Concat(Arr(doc.RootElement, LegacyCallSignsKey)
                .Where(c => !set.RadioCallSigns.Contains(c.Trim(), StringComparer.OrdinalIgnoreCase)))
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MasterDataImportResult(set, dropped);
    }

    private static IReadOnlyList<string> Arr(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetString()!).ToList()
            : Array.Empty<string>();

    private static MasterDataSet ParseRoot(JsonElement root)
    {
        IReadOnlyList<Link> links =
            root.TryGetProperty("links", out var lk) && lk.ValueKind == JsonValueKind.Array
                ? lk.EnumerateArray()
                    .Select(l => new Link(l.GetProperty("name").GetString()!, l.GetProperty("url").GetString()!))
                    .ToList()
                : Array.Empty<Link>();

        var checklists = ParseChecklists(root);

        IReadOnlyList<Vehicle> vehicles =
            root.TryGetProperty("vehicles", out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray()
                    .Select(x => new Vehicle(
                        x.GetProperty("wache").GetString()!,
                        x.GetProperty("callSign").GetString()!,
                        x.TryGetProperty("seats", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0,
                        x.TryGetProperty("hasZugfuehrer", out var hz) && hz.ValueKind == JsonValueKind.True))
                    .ToList()
                : Array.Empty<Vehicle>();

        return new MasterDataSet(
            Arr(root, "roles"),
            Arr(root, "unitStatus"),
            links,
            checklists,
            ParseNavigation(root),
            ParseTruppTypes(root),
            ParsePersonnel(root),
            vehicles,
            ParseSettings(root));
    }

    /// <summary>
    /// Reads the Checkliste templates, newest shape first.
    /// </summary>
    /// <remarks>
    /// Three shapes have to import, because every Stammdaten file in the wild predates the newest:
    /// <list type="number">
    /// <item><c>checklists</c> — 0..n named lists with ids. Wins outright when present.</item>
    /// <item>
    /// <c>checklistTemplateAufbau</c>/<c>checklistTemplateAbbau</c> — mapped onto the frozen ids,
    /// so an imported file's Aufbau list is still the one a Navigation layout names. Either side
    /// is dropped when empty, matching the V23 file migration.
    /// </item>
    /// <item>
    /// <c>checklistTemplate</c> — the oldest flat string array, before the split. Every item
    /// becomes an optional Aufbau item: the safest default, since nothing silently turns into a
    /// blocking requirement.
    /// </item>
    /// </list>
    /// </remarks>
    private static IReadOnlyList<ChecklistTemplate> ParseChecklists(JsonElement root)
    {
        if (root.TryGetProperty("checklists", out var lists) && lists.ValueKind == JsonValueKind.Array)
        {
            return lists.EnumerateArray()
                .Select(l => new ChecklistTemplate(
                    l.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && Guid.TryParse(id.GetString(), out var parsed)
                        ? parsed
                        : Guid.NewGuid(),
                    ChecklistDefaults.TitleOrFallback(
                        l.TryGetProperty("title", out var t) ? t.GetString() : null),
                    Items(l, "items")))
                .ToList();
        }

        if (root.TryGetProperty("checklistTemplateAufbau", out _) || root.TryGetProperty("checklistTemplateAbbau", out _))
        {
            return ChecklistTemplate.AufbauAbbau(
                Items(root, "checklistTemplateAufbau"), Items(root, "checklistTemplateAbbau"));
        }

        if (root.TryGetProperty("checklistTemplate", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
        {
            return ChecklistTemplate.AufbauAbbau(
                legacy.EnumerateArray().Select(x => new ChecklistTemplateItem(x.GetString()!, false)).ToList(),
                Array.Empty<ChecklistTemplateItem>());
        }

        return Array.Empty<ChecklistTemplate>();

        static IReadOnlyList<ChecklistTemplateItem> Items(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray()
                    .Select(x => new ChecklistTemplateItem(
                        x.GetProperty("text").GetString()!,
                        x.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True))
                    .ToList()
                : Array.Empty<ChecklistTemplateItem>();
    }

    /// <summary>
    /// Reads <c>truppTypes</c>, which holds one object per Trupp-Typ: <c>name</c>, <c>memberCount</c>
    /// and <c>maxDurationMinutes</c>.
    /// <para>
    /// A file exported before #398 has a bare string there instead, because the crew size and
    /// Einsatzzeit were still decided by comparing that string against a compiled-in name. Such an
    /// item is translated through <see cref="LegacyTruppTypeDefaults"/> -- the same table the SQLite
    /// widening uses -- so a brigade restoring from a JSON backup and one simply opening its
    /// existing masterdata.db end up with identical rows. The branch is per item rather than per
    /// array: a hand-edited file may legitimately mix the two, and checking costs nothing.
    /// </para>
    /// <para>
    /// <c>memberCount</c> is clamped rather than rejected. This runs on a payload that crossed a
    /// trust boundary (an imported file, or a sync host's <c>/masterdata</c>), and HomeViewModel
    /// catches only the JSON exception types around the latter -- throwing here would escape that
    /// catch and leak the open hub connection instead of failing the join cleanly.
    /// </para>
    /// </summary>
    private static IReadOnlyList<TruppType> ParseTruppTypes(JsonElement root)
    {
        if (!root.TryGetProperty("truppTypes", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TruppType>();
        }

        // Read from the same document, before ParseSettings drops them: a file old enough to list
        // bare names also still carries that brigade's own Einsatzzeiten, and translating its
        // Trupp-Typen with the shipped defaults instead would quietly hand back longer times under
        // air than it had configured.
        var legacy = ParseLegacyEinsatzzeiten(root);
        var result = new List<TruppType>();
        foreach (var x in arr.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.String)
            {
                result.Add(LegacyTruppTypeDefaults.ToTruppType(x.GetString()!, legacy));
                continue;
            }

            result.Add(new TruppType(
                x.GetProperty("name").GetString()!.Trim(),
                TruppType.ClampMemberCount(Int(x, "memberCount", AtemschutzTrupp.StandardMemberCount)),
                TruppType.ClampMaxDurationMinutes(
                    Int(x, "maxDurationMinutes", AtemschutzTrupp.DefaultMaxDurationMinutes))));
        }

        return result;

        static int Int(JsonElement e, string prop, int fallback) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
    }

    /// <summary>
    /// The three retired per-name Einsatzzeit settings, as an older file still carries them.
    /// <see cref="ParseSettings"/> no longer reads them -- they are not part of
    /// <see cref="IncidentSettings"/> any more -- but the legacy Trupp-Typ translation must, or a
    /// brigade that had shortened its CSA-Einsatzzeit would silently get the longer default back.
    /// Clamped like any other duration crossing this boundary.
    /// </summary>
    private static LegacyEinsatzzeiten ParseLegacyEinsatzzeiten(JsonElement root)
    {
        var d = LegacyEinsatzzeiten.Defaults;
        if (!root.TryGetProperty("settings", out var s) || s.ValueKind != JsonValueKind.Object)
        {
            return d;
        }

        return new LegacyEinsatzzeiten(
            Minutes("agtMaxDurationMinutes", d.Agt),
            Minutes("csaMaxDurationMinutes", d.Chemical),
            Minutes("lpaMaxDurationMinutes", d.Lpa));

        int Minutes(string prop, int fallback) => TruppType.ClampMaxDurationMinutes(
            s.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32()
                : fallback);
    }

    /// <summary>
    /// Reads the optional <c>navigation</c> array. A missing key yields an empty layout, which
    /// means "this build's default" — never a materialized copy of it, see <see cref="NavLayout"/>.
    /// A row without <c>visible</c> is visible: a hand-written file that lists modules means
    /// "show these".
    /// </summary>
    private static IReadOnlyList<NavEntry> ParseNavigation(JsonElement root)
    {
        if (!root.TryGetProperty("navigation", out var nav) || nav.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NavEntry>();
        }

        return nav.EnumerateArray().Select(ToEntry).ToList();

        static NavEntry ToEntry(JsonElement e)
        {
            var module = e.TryGetProperty("module", out var m) ? m.GetString() ?? string.Empty : string.Empty;
            var checklistId = e.TryGetProperty("checklistId", out var c)
                && c.ValueKind == JsonValueKind.String
                && Guid.TryParse(c.GetString(), out var id)
                    ? id
                    : (Guid?)null;
            var visible = !e.TryGetProperty("visible", out var v) || v.ValueKind != JsonValueKind.False;
            return new NavEntry(module, checklistId, visible);
        }
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
        {
            return d;
        }

        static int Int(JsonElement e, string prop, int fallback) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

        return new IncidentSettings(
            Int(s, "ilsReminderIntervalMinutes", d.IlsReminderIntervalMinutes),
            Int(s, "ilsReminderFollowUpIntervalMinutes", d.IlsReminderFollowUpIntervalMinutes),
            Int(s, "returnPressureBar", d.ReturnPressureBar));
    }

    private static IReadOnlyList<Person> ParsePersonnel(JsonElement root)
    {
        if (!root.TryGetProperty("personnel", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Person>();
        }

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
        ArgumentNullException.ThrowIfNull(set);
        var model = new
        {
            roles = set.Roles,
            unitStatus = set.UnitStatus,
            truppTypes = set.TruppTypes.Select(t => new
            {
                name = t.Name,
                memberCount = t.MemberCount,
                maxDurationMinutes = t.MaxDurationMinutes,
            }),
            checklists = ChecklistsForExport(set),
            navigation = NavigationForExport(set),
            links = set.Links.Select(l => new { name = l.Name, url = l.Url }),
            vehicles = set.Vehicles.Select(v => new { wache = v.Wache, callSign = v.CallSign, seats = v.Seats, hasZugfuehrer = v.HasZugfuehrer }),
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
                returnPressureBar = set.Settings.ReturnPressureBar,
            },
        };

        return JsonSerializer.Serialize(model, ExportOptions);
    }

    // The legacy checklistTemplateAufbau/Abbau keys are deliberately not written any more: they
    // cannot express a third list, and emitting both shapes would leave two sources of truth in
    // one file. Import still reads them, and the older flat checklistTemplate array.
    private static List<object> ChecklistsForExport(MasterDataSet set) =>
        set.ChecklistTemplates.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            items = t.Items.Select(i => new { text = i.Text, mandatory = i.IsMandatory }),
        }).Cast<object>().ToList();

    private static List<object> NavigationForExport(MasterDataSet set) =>
        set.Navigation.Select(n => new
        {
            module = n.ModuleKey,
            checklistId = n.ChecklistId,
            visible = n.IsVisible,
        }).Cast<object>().ToList();
}

/// <summary>
/// What <see cref="MasterDataJson.ParseForImport"/> read: the set itself plus the legacy Wachen /
/// Funkrufnamen entries that were in the file but have no vehicle or roster person to derive from,
/// and therefore are not part of <see cref="Set"/>.
/// </summary>
public sealed record MasterDataImportResult(MasterDataSet Set, IReadOnlyList<string> DroppedLegacyEntries);
