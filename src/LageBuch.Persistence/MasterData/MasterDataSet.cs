using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;

namespace LageBuch.Persistence.MasterData;

public sealed record MasterDataSet(

    // Funktionen with how often each may be held (#470). Was a bare list of names, in which
    // nothing said that an Einsatzleiter is one person -- the same move #398 made for Trupp-Typen.
    IReadOnlyList<Role> Roles,
    IReadOnlyList<string> UnitStatus,

    // Einsatzstichwörter offered in the Einsatzdaten dialog (#704). Only the chosen text lands in
    // the incident, so renaming or deleting one here never changes a past Einsatz.
    IReadOnlyList<string> Keywords,

    // Ortsteile and Straßen offered in the Einsatzdaten dialog (#461). Two lists, because an
    // Ortsteil need not have a street listed for it -- a Weiler is often addressed by its own name.
    // As with the Stichwörter, only the chosen text lands in the incident.
    IReadOnlyList<string> Districts,
    IReadOnlyList<Street> Streets,
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

    // Textbausteine: phrases called up by typing ::kürzel, each offered in the areas it is ticked
    // for. Inserting one copies its text into the entry (#337); nothing points back here.
    IReadOnlyList<TextSnippet> TextSnippets,

    // Objekte in the Ausrückebereich (#420), picked when a Haus is added to the CO-Messprotokoll.
    // Their structure and labels are copied into the incident (#337); nothing points back here.
    IReadOnlyList<Premises> Premises,

    // Geräte offered in the Ausgabeliste: the Mobilteile, Westen and the AED the ELW hands out,
    // each with how many there are. Only the chosen name lands in the incident; the count is a live
    // rule, checked against today's Stammdaten.
    IReadOnlyList<Device> Devices,
    // Funkgruppen (TMO/DMO) offered as suggestions on the Führungstafel. Only the chosen text
    // lands in an incident, so renaming one here never changes a past Einsatz.
    IReadOnlyList<TalkGroup> TalkGroups,

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
        Array.Empty<Role>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<Street>(),
        Array.Empty<Link>(),
        Array.Empty<ChecklistTemplate>(),
        Array.Empty<NavEntry>(),
        Array.Empty<TruppType>(),
        Array.Empty<Person>(),
        Array.Empty<Vehicle>(),
        Array.Empty<TextSnippet>(),
        Array.Empty<Premises>(),
        Array.Empty<Device>(),
        Array.Empty<TalkGroup>(),
        IncidentSettings.Defaults);

    /// <summary>
    /// True when no category holds a single entry. A fresh install starts here, and it is the
    /// condition under which the Stammdaten editor's Import runs straight away instead of asking
    /// first whether it may replace what is already there.
    /// <see cref="Settings"/> deliberately does not count: it always carries defaults, and letting it
    /// mark the set non-empty would ask for confirmation on an otherwise fresh install.
    /// <see cref="Navigation"/> is excluded for exactly the same reason — a layout saved once would
    /// otherwise permanently turn the silent bootstrap into a confirm prompt.
    /// </summary>
    public bool IsEmpty =>
        Roles.Count == 0
        && UnitStatus.Count == 0
        && Keywords.Count == 0
        && Districts.Count == 0
        && Streets.Count == 0
        && Links.Count == 0 && ChecklistTemplates.Count == 0
        && TruppTypes.Count == 0
        && Personnel.Count == 0
        && Vehicles.Count == 0
        && TextSnippets.Count == 0
        && Premises.Count == 0
        && Devices.Count == 0
        && TalkGroups.Count == 0;

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

/// <summary>
/// A named link — Stammdaten entry so useful external resources can be opened from an Einsatz.
/// Group is a free-text heading the Links tab gathers links under (#518), e.g. "Gefahrgut";
/// empty means ungrouped. It trails and defaults, so older files and sync hosts read as ungrouped.
/// </summary>
[SuppressMessage("Design", "CA1054", Justification = "Link URLs are free-form display data in persisted master data; System.Uri would make non-parseable values (relay or relative links) fail to load.")]
[SuppressMessage("Design", "CA1056", Justification = "Link URLs are free-form display data in persisted master data; System.Uri would make non-parseable values (relay or relative links) fail to load.")]
public sealed record Link(string Name, string Url, string Group = "");

/// <summary>
/// A Textbaustein: a phrase called up by typing <c>::</c> and its <see cref="Shortcut"/> in a text
/// field, e.g. <c>::stl</c> for "Stromleitung beschädigt, Bereich abgesperrt, EVU alarmiert".
/// <c>[…]</c> in the text marks a gap the Funker fills in; the field selects the first one.
/// <see cref="Category"/> is a free-text heading shown beside the suggestion.
/// <para>
/// One list serves every area, and each flag says whether one area offers the Textbaustein:
/// <see cref="ForEtb"/> the ETB fields, <see cref="ForTasks"/> the Aufgabe fields. A Rückmeldung
/// is rarely an Aufgabe, so most are ticked for one; a phrase both use is kept once. A further
/// area is one more flag. A Textbaustein ticked for none is kept, just offered nowhere.
/// </para>
/// </summary>
public sealed record TextSnippet(
    string Shortcut,
    string Text,
    string Category = "",
    bool ForEtb = true,
    bool ForTasks = true)
{
    /// <summary>
    /// The longest text a Textbaustein may carry: what an Aufgabe may hold, the shorter of the two
    /// fields it is typed into. Anything longer only fails when the entry is saved.
    /// </summary>
    public const int MaxTextLength = IncidentTask.MaxTextLength;

    /// <summary>The longest Kürzel; it is typed, so it is short by nature.</summary>
    public const int MaxShortcutLength = 12;

    /// <summary>
    /// The Kürzel as it is matched: lower case, letters and digits only, at most
    /// <see cref="MaxShortcutLength"/>. "::STL", " stl " and "s-t-l" all become "stl", so a
    /// hand-edited file cannot hold a Kürzel nobody can type.
    /// </summary>
    public static string NormalizeShortcut(string? shortcut)
    {
        var letters = new string((shortcut ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return letters.Length > MaxShortcutLength ? letters[..MaxShortcutLength] : letters;
    }

    /// <summary>Trims the text and cuts it to <see cref="MaxTextLength"/>.</summary>
    public static string CapText(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length > MaxTextLength ? trimmed[..MaxTextLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// Brings a list to the shape every consumer may rely on: Kürzel normalized, text capped,
    /// entries without either dropped, and each Kürzel once -- the first wins, as it would in the
    /// suggestion list. The import, a sync host's payload and the store all go through this.
    /// </summary>
    public static IReadOnlyList<TextSnippet> Normalize(IEnumerable<TextSnippet> snippets)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TextSnippet>();
        foreach (var s in snippets)
        {
            var shortcut = NormalizeShortcut(s.Shortcut);
            var text = CapText(s.Text);
            if (shortcut.Length > 0 && text.Length > 0 && seen.Add(shortcut))
            {
                result.Add(s with { Shortcut = shortcut, Text = text, Category = (s.Category ?? string.Empty).Trim() });
            }
        }

        return result;
    }
}

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
    int ReturnPressureBar,

    // The Leitstelle the Rückmeldung goes to (#400). "ILS" is the Bavarian name; elsewhere it is a
    // Kreisleitstelle, a Rettungsleitstelle or plain Leitstelle.
    string DispatchCentreName = IncidentSettings.DefaultDispatchCentreName)
{
    public const string DefaultDispatchCentreName = "ILS";

    /// <summary>
    /// Long enough for "Integrierte Leitstelle Fürstenfeldbruck"; the name sits in the header row, and
    /// it arrives from a Stammdaten import and from a sync host, both untrusted.
    /// </summary>
    public const int MaxDispatchCentreNameLength = 40;

    private readonly string _dispatchCentreName = NormalizeDispatchCentreName(DispatchCentreName);

    /// <summary>Trimmed, never blank (falls back to "ILS") and at most <see cref="MaxDispatchCentreNameLength"/> long.</summary>
    public string DispatchCentreName
    {
        get => _dispatchCentreName;
        init => _dispatchCentreName = NormalizeDispatchCentreName(value);
    }

    private static string NormalizeDispatchCentreName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return DefaultDispatchCentreName;
        }

        return trimmed.Length > MaxDispatchCentreNameLength ? trimmed[..MaxDispatchCentreNameLength].TrimEnd() : trimmed;
    }

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
/// How often a single Funktion may be held at once (#470). <see cref="Multiple"/> is the default
/// and keeps today's behaviour, in which any number of people hold the same Funktion.
/// <para>
/// The other two are what a brigade marks the leadership positions with: an Einsatzleiter is one
/// person per Einsatz, an Abschnittsleiter one per Abschnitt. The setting is advisory at the moment
/// of entry -- it is enforced by <c>RolesViewModel</c>, not by the aggregate, because the catalogue
/// lives in <c>LageBuch.Persistence</c> and <c>LageBuch.Domain</c> references nothing.
/// </para>
/// </summary>
public enum RoleUniqueness
{
    /// <summary>No limit -- any number of people hold this Funktion.</summary>
    Multiple,

    /// <summary>One running holder for the whole Einsatz, whatever the Abschnitt.</summary>
    UniquePerIncident,

    /// <summary>One running holder per Abschnitt. An empty Abschnitt is a bucket of its own.</summary>
    UniquePerSection,
}

/// <summary>
/// One Funktion from the Stammdaten: the name offered on the Funktionen tab, and how often it may
/// be held (#470). Was a bare list of names, so a brigade had nowhere to say that EL is one person.
/// <para>
/// <see cref="UniquenessName"/> and <see cref="UniquenessFrom"/> are the file format's spelling, and
/// are read back and forth by <see cref="MasterDataJson"/> and <see cref="MasterDataStore"/>. They
/// take and return plain strings rather than a <c>JsonElement</c> so this record stays free of
/// System.Text.Json, and they are total in both directions: an unrecognised name reads as
/// <see cref="RoleUniqueness.Multiple"/> and never throws, because a file crosses a trust boundary
/// and <c>HomeViewModel</c> catches only the JSON exception types around the master-data parse.
/// </para>
/// </summary>
public sealed record Role(string Name, RoleUniqueness Uniqueness = RoleUniqueness.Multiple, RoleSlot Slot = RoleSlot.None)
{
    /// <summary>The spelling <see cref="UniquenessFrom"/> reads back.</summary>
    public static string UniquenessName(RoleUniqueness uniqueness) => uniqueness switch
    {
        RoleUniqueness.UniquePerIncident => "uniquePerIncident",
        RoleUniqueness.UniquePerSection => "uniquePerSection",
        _ => "multiple",
    };

    /// <summary>
    /// The mode a stored name means. Case-insensitive, because these files are hand-edited, and
    /// strict about the name itself: a bare "1" is a mistake to report, not a mode to honour. The
    /// fold is upper-case because CA1308 rejects <c>ToLowerInvariant</c>, and for a fixed ASCII
    /// token the two are the same comparison; only the matched literals are spelled this way. The
    /// file's own spelling stays the camelCase <see cref="UniquenessName"/> writes.
    /// </summary>
    public static RoleUniqueness UniquenessFrom(string? name) => name?.Trim().ToUpperInvariant() switch
    {
        "UNIQUEPERINCIDENT" => RoleUniqueness.UniquePerIncident,
        "UNIQUEPERSECTION" => RoleUniqueness.UniquePerSection,
        _ => RoleUniqueness.Multiple,
    };
}

/// <summary>
/// A person from the local roster. Personal data (names, mobile numbers) that must never be
/// compiled into the app, so it only ever reaches a running install through an explicit import.
/// The roster is empty until then — every consumer must treat that as normal rather than as a
/// configuration error, and must still accept a freely typed name.
/// IsOwn tells the brigade's own people from those of a neighbouring one (#458); only own
/// personnel are offered as Lagebuchführer. Defaulted to own, because everything entered before
/// the flag existed was entered as the brigade's own.
/// Email and Note (#451) trail IsOwn and default to null, so no existing call site changes; the
/// Note is where the roster records what somebody is responsible for.
/// </summary>
public sealed record Person(
    string LastName,
    string FirstName,
    string? Role,
    string? CallSign,
    string? Phone,
    bool IsOwn = true,
    string? Email = null,
    string? Note = null)
{
    /// <summary>How the person is offered in pickers and stored on an assignment.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FirstName) ? LastName : $"{LastName}, {FirstName}";

    /// <summary>
    /// Whether a value is really there, for the Kontakte view's per-field visibility.
    /// </summary>
    /// <remarks>
    /// Deliberately not a null check. <c>MasterDataJson</c>'s optional-string reader hands through
    /// whatever the file holds, so a blank field arrives as "" or "   " rather than as null -- the
    /// roster has a person whose Funkrufname is exactly that. Binding a button's IsVisible to
    /// ObjectConverters.IsNotNull would light it up for an address nobody can write to.
    /// </remarks>
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

    /// <inheritdoc cref="HasEmail"/>
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    /// <inheritdoc cref="HasEmail"/>
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

/// <summary>
/// One vehicle of a Wache (#76). The Wache is the brigade's name as free text — the set's Wachen
/// (<see cref="MasterDataSet.Brigades"/>) and Funkrufnamen (<see cref="MasterDataSet.RadioCallSigns"/>)
/// are derived from these rows, so the vehicle list is the single place to maintain them. The
/// seat count feeds the Stärke preset when the vehicle is picked in the Kräfte entry. HasZugfuehrer marks a
/// command vehicle (ELW/KdoW) that carries the Zugführer -- unlike Officer/Mannschaft, ZF is
/// not seat-derived, since only specific vehicles carry one. Defaulted so existing call sites
/// and older Stammdaten payloads keep working unchanged. IsOwn tells the brigade's own vehicles
/// from those of a neighbouring one (#458); defaulted to own for the same reason, which is the
/// opposite of HasZugfuehrer's default -- a missing flag must not turn a whole fleet foreign.
/// Phone is the Fahrzeugtelefon and Email the vehicle's mailbox (an ELW's, a Wache's), both
/// optional and trailing for the same reason; a vehicle that has either is listed in the Kontakte
/// tab next to the roster.
/// </summary>
public sealed record Vehicle(
    string Wache,
    string CallSign,
    int Seats,
    bool HasZugfuehrer = false,
    bool IsOwn = true,
    string? Phone = null,
    string? Email = null)
{
    /// <inheritdoc cref="Person.HasPhone"/>
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    /// <inheritdoc cref="Person.HasEmail"/>
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
}

/// <summary>Whether a Funkgruppe is a Truppmittel-Funk (TMO) or a Direktmodus group (DMO).</summary>
public enum TalkGroupKind
{
    Tmo,
    Dmo,
}

/// <summary>
/// One Funkgruppe from the Stammdaten, e.g. "TMO FW_MUSTER 1000" or "DMO 301_F". Number is the
/// optional channel or group number and Note free text; both are null or blank when not given.
/// </summary>
public sealed record TalkGroup(string Name, string? Number, TalkGroupKind Kind, string? Note = null)
{
    /// <summary>The kind upper-case, then the name, then the number when present.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Number)
            ? $"{KindLabel(Kind)} {Name}"
            : $"{KindLabel(Kind)} {Name} {Number}";

    /// <summary>The label shown for <paramref name="kind"/>.</summary>
    public static string KindLabel(TalkGroupKind kind) => kind == TalkGroupKind.Dmo ? "DMO" : "TMO";

    /// <summary>The file format's spelling, read back by <see cref="KindFrom"/>.</summary>
    public static string KindName(TalkGroupKind kind) => kind == TalkGroupKind.Dmo ? "dmo" : "tmo";

    /// <summary>Total: an unknown or missing kind reads as <see cref="TalkGroupKind.Tmo"/>.</summary>
    public static TalkGroupKind KindFrom(string? name) =>
        string.Equals(name?.Trim(), "dmo", StringComparison.OrdinalIgnoreCase) ? TalkGroupKind.Dmo : TalkGroupKind.Tmo;
}

/// <summary>
/// One street of the Gemeinde (#461), with the Ortsteil, PLZ and Ort it lies in where those are
/// known. Picking it in the Einsatzdaten fills those fields when every row of that name agrees, so
/// a street that runs through two Ortsteile or PLZ is two rows. Every part but the name may be
/// blank: OpenStreetMap, where most lists come from, rarely says which Ortsteil a street is in.
/// </summary>
public sealed record Street(string Name, string District = "", string PostalCode = "", string Place = "");

/// <summary>
/// An Objekt (#420): a building in the Ausrückebereich, known long before the call. It has an
/// address and the structure a Haus in the CO-Messprotokoll is created with: Obergeschosse,
/// Wohnungen je Geschoss, Untergeschosse and the Wohnungsbezeichnungen, keyed like
/// <see cref="Building.ApartmentLabels"/>. Picking it copies the structure and labels into the
/// incident (#337). The address is not copied, because a Haus has none; it is there to tell two
/// Objekte apart, and for #427 to hang Einsatzpläne on.
/// <para>
/// Like <see cref="TruppType"/>, this record does not throw on a structure a Haus cannot have.
/// <see cref="Normalize"/> clamps it at every way in instead.
/// </para>
/// </summary>
public sealed record Premises(
    string Name,
    string Street = "",
    string HouseNumber = "",
    string PostalCode = "",
    string Place = "",
    int FloorCount = Premises.DefaultFloorCount,
    int ApartmentsPerFloor = Premises.DefaultApartmentsPerFloor,
    int UndergroundFloorCount = Premises.DefaultUndergroundFloorCount)
{
    /// <summary>The structure a new Haus starts from, in the CO-Messprotokoll dialog and in a new
    /// Objekt alike.</summary>
    public const int DefaultFloorCount = 8;
    public const int DefaultApartmentsPerFloor = 10;
    public const int DefaultUndergroundFloorCount = 1;

    /// <summary>How many Objekte a list may hold. A Gemeinde has dozens; the cap only bounds what a
    /// file or a sync host can hand over.</summary>
    public const int MaxCount = 500;

    private static readonly IReadOnlyDictionary<string, string?> NoLabels = new Dictionary<string, string?>();

    /// <summary>The Wohnungsbezeichnungen, keyed by <see cref="CoMeasurementLabels.ApartmentLabelKey"/>.</summary>
    public IReadOnlyDictionary<string, string?> ApartmentLabels { get; init; } = NoLabels;

    /// <summary>The address on one line, e.g. "Musterstraße 5, 82256 Musterstadt"; blank parts are
    /// left out.</summary>
    public string AddressLine =>
        string.Join(
            ", ",
            new[] { $"{Street} {HouseNumber}".Trim(), $"{PostalCode} {Place}".Trim() }.Where(p => p.Length > 0));

    /// <summary>
    /// Brings a list to the shape every consumer may rely on: text trimmed, structure clamped into
    /// what a Haus may have, labels kept only where they fit it, entries without a name dropped,
    /// each name once (case-insensitively; the first wins) and at most <see cref="MaxCount"/>. The
    /// import, a sync host's payload, the store and the editor all go through this.
    /// </summary>
    public static IReadOnlyList<Premises> Normalize(IEnumerable<Premises> premises)
    {
        ArgumentNullException.ThrowIfNull(premises);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Premises>();
        foreach (var p in premises)
        {
            var name = p.Name.Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            var floors = Math.Clamp(p.FloorCount, Building.MinFloorCount, Building.MaxFloorCount);
            var apartments = Math.Clamp(p.ApartmentsPerFloor, Building.MinApartmentsPerFloor, Building.MaxApartmentsPerFloor);
            var underground = Math.Clamp(p.UndergroundFloorCount, 0, Building.MaxUndergroundFloorCount);
            result.Add(p with
            {
                Name = name,
                Street = p.Street.Trim(),
                HouseNumber = p.HouseNumber.Trim(),
                PostalCode = p.PostalCode.Trim(),
                Place = p.Place.Trim(),
                FloorCount = floors,
                ApartmentsPerFloor = apartments,
                UndergroundFloorCount = underground,
                ApartmentLabels = CoMeasurementLabels.LabelsWithin(p.ApartmentLabels, floors, apartments, underground),
            });

            if (result.Count == MaxCount)
            {
                break;
            }
        }

        return result;
    }

    // A record compares a dictionary by reference; two Objekte with the same labels are the same.
    public bool Equals(Premises? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Street, other.Street, StringComparison.Ordinal)
        && string.Equals(HouseNumber, other.HouseNumber, StringComparison.Ordinal)
        && string.Equals(PostalCode, other.PostalCode, StringComparison.Ordinal)
        && string.Equals(Place, other.Place, StringComparison.Ordinal)
        && FloorCount == other.FloorCount
        && ApartmentsPerFloor == other.ApartmentsPerFloor
        && UndergroundFloorCount == other.UndergroundFloorCount
        && ApartmentLabels.Count == other.ApartmentLabels.Count
        && ApartmentLabels.All(kv =>
            other.ApartmentLabels.TryGetValue(kv.Key, out var label) && string.Equals(label, kv.Value, StringComparison.Ordinal));

    public override int GetHashCode() =>
        HashCode.Combine(Name, Street, HouseNumber, PostalCode, Place, FloorCount, ApartmentsPerFloor, UndergroundFloorCount);
}

/// <summary>
/// One Gerät the ELW hands out, and how many of it there are. The Ausgabeliste compares the count
/// against what is still out, so a third Weste EL handed out when the ELW carries two is questioned
/// before it is recorded. 0 means not counted: "Sonstiges (siehe Bemerkung)" has no stock to check.
/// <para>
/// Like <see cref="TruppType"/>, this does not throw on an impossible count; every entrance clamps
/// through <see cref="ClampCount"/> instead, since the parse runs on input that crossed a trust
/// boundary.
/// </para>
/// </summary>
public sealed record Device(string Name, int Count)
{
    /// <summary>The largest count the editor offers and every entrance accepts.</summary>
    public const int MaxCount = 999;

    /// <summary><paramref name="count"/> forced into 0..<see cref="MaxCount"/>.</summary>
    public static int ClampCount(int count) => Math.Clamp(count, 0, MaxCount);
}

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

    // .example is the RFC 2606 reserved TLD: an invented "@ff-musterstadt.de" could well be a
    // real domain, and these strings end up in screenshots and in the published sample Stammdaten.
    public const string EmailAddress = "max.mustermann@ff-musterstadt.example";
    public const string EmailAddressAlt = "erika.musterfrau@ff-musterstadt.example";

    // A vehicle's mailbox reads as a function, never as a person's name.
    public const string VehicleEmailAddress = "elw1@ff-musterstadt.example";

    // The two shapes a roster Notiz actually takes: a Fachgebiet, and the Feuerwehren somebody
    // covers. Both are here so a fixture can exercise the Kontakte search against either.
    public const string PersonNote = "KBM Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz";
    public const string PersonNoteAlt = "Musterdorf, Beispielried, Vorlagenhofen, Schemastetten";

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
    public const string LinkGroup = "Gefahrgut";
    public const string Whereabouts = "bei Nachbarin Frau Meier";
    public const string RescueCallSign = "Rotkreuz Musterstadt 71/1";

    // Derived placeholder strings. Compile-time const concatenation, so the "z. B." prefix and
    // the underlying value can never drift apart from one another.
    public const string CallSignPlaceholder = "z. B. " + CallSign;
    public const string SecondCallSignPlaceholder = "z. B. " + SecondCallSign;
    public const string BrigadePlaceholder = "z. B. " + Brigade;
    public const string PersonLastNamePlaceholder = "z. B. " + PersonLastName;
    public const string PersonFirstNamePlaceholder = "z. B. " + PersonFirstName;
    public const string PersonDisplayNamePlaceholder = "z. B. " + PersonLastName + ", " + PersonFirstName;
    public const string PhoneNumberPlaceholder = "z. B. " + PhoneNumber;
    public const string EmailPlaceholder = "z. B. " + EmailAddress;
    public const string VehicleEmailPlaceholder = "z. B. " + VehicleEmailAddress;
    public const string PersonNotePlaceholder = "z. B. " + PersonNote;
    public const string WhereaboutsPlaceholder = "z. B. " + Whereabouts + " oder im RTW " + RescueCallSign;
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
    public const string LinkGroupPlaceholder = "z. B. " + LinkGroup;

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
        new Person(PersonLastName, PersonFirstName, "ZF", "Land 1", PhoneNumber, Email: EmailAddress, Note: PersonNote),
        new Person(PersonLastNameAlt, PersonFirstNameAlt, "GF", null, PhoneNumberAlt, Email: EmailAddressAlt, Note: PersonNoteAlt),
    };

    public static readonly IReadOnlyList<Link> Links = new[]
    {
        new Link(LinkName, LinkUrl),
        new Link("ERICard", "https://example.org/ericard", LinkGroup),
        new Link("GESTIS-Stoffdatenbank", "https://example.org/gestis", LinkGroup),
        new Link("Kartendienst", "https://example.org/karte", "Karten"),
    };

    // Objekte (#420) in the same invented Musterstadt as the sample Stammdaten's Straßen.
    // Stryker disable all : fixture data; every test reads these values back through the fixture
    // itself, so any other invented value is an equally good fixture, not a defect.
    public static readonly IReadOnlyList<Premises> Premises = new[]
    {
        new Premises("Wohnanlage Lindenallee", "Lindenallee", "12", "12345", "Musterstadt", 4, 3, 1)
        {
            ApartmentLabels = new Dictionary<string, string?>
            {
                ["0:1"] = "Hausmeister",
                ["0:3"] = "Kinderwagen",
                ["-1:1"] = "Heizung",
                ["-1:2"] = "Waschküche",
            },
        },
        new Premises(BuildingName, "Hauptstraße", "5", "12345", "Musterstadt", 3, 2, 0),
    };

    // Stryker restore all
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
    private const int MaxTalkGroupNameLength = 200;

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

    /// <summary>
    /// The required name of a hand-edited object entry, trimmed.
    /// </summary>
    /// <remarks>
    /// Absent (<c>GetProperty</c> throws <see cref="KeyNotFoundException"/>) and wrong-kind
    /// (<c>InvalidOperationException</c>) both land inside the filter
    /// <c>HomeViewModel.JoinDeviceAsync</c> applies around this parse, so the caller turns them
    /// into a German banner and disposes the session. An *explicit* JSON null does not: there
    /// <c>GetString()</c> returns null and the next <c>Trim()</c> raised a
    /// <see cref="NullReferenceException"/> that escaped that filter, skipped the
    /// <c>DisposeAsync</c> in its catch and killed the joining app mid-Einsatz. The property is
    /// present and unusable, which is a malformed document -- so say so in the one exception type
    /// the caller already handles, and in German, because the caller shows this message to the
    /// Lagebuchführer.
    /// </remarks>
    private static string RequiredName(JsonElement entry, string what) =>
        entry.GetProperty("name").GetString()
        ?? throw new JsonException($"{what}: \"name\" darf nicht null sein.");

    // The per-item branch #398 wrote for Trupp-Typen: a bare string is what every file written
    // before #470 holds and must keep importing, an object is the current form. name is required
    // (GetProperty throws KeyNotFoundException, which HomeViewModel catches; RequiredName covers
    // the null it would otherwise pass through); uniqueness is optional and degrades to Multiple,
    // because a hand-edited file may carry anything.
    private static IReadOnlyList<Role> ParseRoles(JsonElement root)
    {
        if (!root.TryGetProperty("roles", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Role>();
        }

        var result = new List<Role>();
        foreach (var x in arr.EnumerateArray())
        {
            result.Add(x.ValueKind == JsonValueKind.String
                ? new Role(x.GetString()!.Trim())
                : new Role(
                    RequiredName(x, "Funktion"),
                    Role.UniquenessFrom(
                        x.TryGetProperty("uniqueness", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString()
                            : null),
                    LeadershipKeys.SlotFrom(
                        x.TryGetProperty("slot", out var sl) && sl.ValueKind == JsonValueKind.String
                            ? sl.GetString()
                            : null)));
        }

        return result;
    }

    // Hostile input: name is required and capped, kind degrades to TMO, and blank optional strings
    // (which arrive as "" rather than null) are normalised to null.
    private static IReadOnlyList<TalkGroup> ParseTalkGroups(JsonElement root)
    {
        if (!root.TryGetProperty("talkGroups", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TalkGroup>();
        }

        var result = new List<TalkGroup>();
        foreach (var x in arr.EnumerateArray())
        {
            var name = RequiredName(x, "Funkgruppe").Trim();
            if (name.Length > MaxTalkGroupNameLength)
            {
                throw new JsonException($"Funkgruppe: \"name\" darf höchstens {MaxTalkGroupNameLength} Zeichen lang sein.");
            }

            result.Add(new TalkGroup(
                name,
                BlankToNull(OptionalString(x, "number")),
                TalkGroup.KindFrom(OptionalString(x, "kind")),
                BlankToNull(OptionalString(x, "note"))));
        }

        return result;
    }

    private static string? OptionalString(JsonElement e, string key) =>
        e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MasterDataSet ParseRoot(JsonElement root)
    {
        IReadOnlyList<Link> links =
            root.TryGetProperty("links", out var lk) && lk.ValueKind == JsonValueKind.Array
                ? lk.EnumerateArray()
                    .Select(l => new Link(l.GetProperty("name").GetString()!, l.GetProperty("url").GetString()!, LinkGroup(l)))
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
                        x.TryGetProperty("hasZugfuehrer", out var hz) && hz.ValueKind == JsonValueKind.True,
                        IsOwn(x),
                        Opt(x, "phone"),
                        Opt(x, "email")))
                    .ToList()
                : Array.Empty<Vehicle>();

        return new MasterDataSet(
            ParseRoles(root),
            Arr(root, "unitStatus"),
            Arr(root, "keywords"),
            Arr(root, "districts"),
            ParseStreets(root),
            links,
            checklists,
            ParseNavigation(root),
            ParseTruppTypes(root),
            ParsePersonnel(root),
            vehicles,
            ParseTextSnippets(root),
            ParsePremises(root),
            ParseDevices(root),
            ParseTalkGroups(root),
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
                RequiredName(x, "Trupp-Typ"),
                TruppType.ClampMemberCount(Int(x, "memberCount", AtemschutzTrupp.StandardMemberCount)),
                TruppType.ClampMaxDurationMinutes(
                    Int(x, "maxDurationMinutes", AtemschutzTrupp.DefaultMaxDurationMinutes))));
        }

        return result;

        static int Int(JsonElement e, string prop, int fallback) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
    }

    /// <summary>
    /// Reads the optional <c>devices</c> array: one object per Gerät with a required <c>name</c>
    /// and an optional <c>count</c>, clamped rather than rejected for the reason
    /// <see cref="ParseTruppTypes"/> gives. A bare string is a Gerät nobody counted.
    /// </summary>
    private static IReadOnlyList<Device> ParseDevices(JsonElement root)
    {
        if (!root.TryGetProperty("devices", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Device>();
        }

        return arr.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String
                ? new Device((x.GetString() ?? string.Empty).Trim(), 0)
                : new Device(
                    RequiredName(x, "Gerät").Trim(),
                    Device.ClampCount(
                        x.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0)))
            .ToList();
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
    /// Reads the optional <c>textSnippets</c> array: one object per Textbaustein with
    /// <c>shortcut</c>, <c>text</c>, an optional <c>category</c> and the areas it is offered in,
    /// <c>etb</c> and <c>tasks</c>. Normalized like everything else that crosses this boundary, see
    /// <see cref="TextSnippet.Normalize"/>; a null shortcut or text is a malformed document.
    /// </summary>
    /// <remarks>
    /// Only an explicit <c>false</c> unticks an area: a hand-written entry that names no area means
    /// "offer this", and one that silently showed up nowhere would look like a broken import.
    /// </remarks>
    private static IReadOnlyList<TextSnippet> ParseTextSnippets(JsonElement root)
    {
        if (!root.TryGetProperty("textSnippets", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TextSnippet>();
        }

        return TextSnippet.Normalize(arr.EnumerateArray().Select(x => new TextSnippet(
            Required(x, "shortcut"),
            Required(x, "text"),
            x.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty,
            Area(x, "etb"),
            Area(x, "tasks"))));

        static bool Area(JsonElement e, string prop) =>
            !e.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.False;

        static string Required(JsonElement e, string prop) =>
            e.GetProperty(prop).GetString()
            ?? throw new JsonException($"Textbaustein: \"{prop}\" darf nicht null sein.");
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

        // IncidentSettings trims, caps and replaces a blank name itself.
        static string? Str(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        return new IncidentSettings(
            Int(s, "ilsReminderIntervalMinutes", d.IlsReminderIntervalMinutes),
            Int(s, "ilsReminderFollowUpIntervalMinutes", d.IlsReminderFollowUpIntervalMinutes),
            Int(s, "returnPressureBar", d.ReturnPressureBar),
            Str(s, "dispatchCentreName") ?? d.DispatchCentreName);
    }

    // A street list is typically generated from OpenStreetMap and runs to hundreds of rows, so one
    // malformed row is skipped rather than failing the whole import: a row without a name says
    // nothing, and a field that is not a string reads as blank.
    private static IReadOnlyList<Street> ParseStreets(JsonElement root)
    {
        if (!root.TryGetProperty("streets", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Street>();
        }

        var result = new List<Street>();
        foreach (var x in arr.EnumerateArray())
        {
            var name = Text(x, "name");
            if (name.Length > 0)
            {
                result.Add(new Street(name, Text(x, "district"), Text(x, "postalCode"), Text(x, "place")));
            }
        }

        return result;

        static string Text(JsonElement e, string prop) =>
            e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(prop, out var v)
            && v.ValueKind == JsonValueKind.String
            && v.GetString() is { } text
                ? text.Trim()
                : string.Empty;
    }

    /// <summary>
    /// Reads the optional <c>premises</c> array (#420): one object per Objekt with <c>name</c>, the
    /// address (<c>street</c>, <c>houseNumber</c>, <c>postalCode</c>, <c>place</c>), the structure
    /// (<c>floors</c>, <c>apartmentsPerFloor</c>, <c>undergroundFloors</c>) and
    /// <c>apartmentLabels</c>, an object from <c>"floor:apartment"</c> to the Bezeichnung.
    /// </summary>
    /// <remarks>
    /// Tolerant like the Straßen: a row without a name is skipped, a field of the wrong kind reads
    /// as blank or as the default, and <see cref="Premises.Normalize"/> clamps the rest. A sync
    /// client parses this from a peer, and nothing here may throw outside the exceptions
    /// <c>HomeViewModel.JoinDeviceAsync</c> catches.
    /// </remarks>
    private static IReadOnlyList<Premises> ParsePremises(JsonElement root)
    {
        if (!root.TryGetProperty("premises", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Premises>();
        }

        return Premises.Normalize(arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new Premises(
                Text(x, "name"),
                Text(x, "street"),
                Text(x, "houseNumber"),
                Text(x, "postalCode"),
                Text(x, "place"),
                Number(x, "floors", Premises.DefaultFloorCount),
                Number(x, "apartmentsPerFloor", Premises.DefaultApartmentsPerFloor),
                Number(x, "undergroundFloors", Premises.DefaultUndergroundFloorCount))
            {
                ApartmentLabels = Labels(x),
            }));

        static string Text(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } text
                ? text
                : string.Empty;

        static int Number(JsonElement e, string prop, int fallback) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

        // TryAdd, not ToDictionary: a key twice in one object is valid JSON, and the
        // ArgumentException ToDictionary throws on it escapes the join's catch filter.
        static Dictionary<string, string?> Labels(JsonElement e)
        {
            var labels = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (e.TryGetProperty("apartmentLabels", out var v) && v.ValueKind == JsonValueKind.Object)
            {
                foreach (var l in v.EnumerateObject().Where(l => l.Value.ValueKind == JsonValueKind.String))
                {
                    labels.TryAdd(l.Name, l.Value.GetString());
                }
            }

            return labels;
        }
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
                Opt(p, "phone"),
                IsOwn(p),
                Opt(p, "email"),
                Opt(p, "note")))
            .ToList();
    }

    /// <summary>An optional string field of a vehicle or a person: absent or null reads as null.</summary>
    private static string? Opt(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind is not JsonValueKind.Null ? v.GetString() : null;

    /// <summary>
    /// A link's optional group (#518), trimmed. Absent — every file and sync host from before the
    /// field — null, or not a string all read as ungrouped rather than failing the whole import.
    /// </summary>
    private static string LinkGroup(JsonElement link) =>
        link.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String && g.GetString() is { } group
            ? group.Trim()
            : string.Empty;

    /// <summary>
    /// Only an explicit <c>false</c> marks an entry foreign (#458): a file or a sync host from before
    /// the flag existed carries no isOwn at all, and everything in it is the brigade's own.
    /// </summary>
    private static bool IsOwn(JsonElement e) =>
        !(e.TryGetProperty("isOwn", out var o) && o.ValueKind == JsonValueKind.False);

    /// <summary>
    /// Serializes the whole set in the superset schema, so a file written here re-parses identically.
    /// Indented and with relaxed escaping so umlauts and slashes stay readable in a hand-edited file.
    /// </summary>
    public static string Serialize(MasterDataSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var model = new
        {
            roles = set.Roles.Select(r => new
            {
                name = r.Name,
                uniqueness = Role.UniquenessName(r.Uniqueness),
                slot = LeadershipKeys.SlotKey(r.Slot),
            }),
            unitStatus = set.UnitStatus,
            keywords = set.Keywords,
            devices = set.Devices.Select(d => new { name = d.Name, count = d.Count }),
            districts = set.Districts,
            streets = set.Streets.Select(t => new { name = t.Name, district = t.District, postalCode = t.PostalCode, place = t.Place }),
            truppTypes = set.TruppTypes.Select(t => new
            {
                name = t.Name,
                memberCount = t.MemberCount,
                maxDurationMinutes = t.MaxDurationMinutes,
            }),
            checklists = ChecklistsForExport(set),
            navigation = NavigationForExport(set),
            links = set.Links.Select(l => new { name = l.Name, url = l.Url, group = l.Group }),
            vehicles = set.Vehicles.Select(v => new { wache = v.Wache, callSign = v.CallSign, seats = v.Seats, hasZugfuehrer = v.HasZugfuehrer, isOwn = v.IsOwn, phone = v.Phone, email = v.Email }),
            talkGroups = set.TalkGroups.Select(t => new
            {
                name = t.Name,
                number = t.Number,
                kind = TalkGroup.KindName(t.Kind),
                note = t.Note,
            }),
            personnel = set.Personnel.Select(p => new
            {
                lastName = p.LastName,
                firstName = p.FirstName,
                role = p.Role,
                callSign = p.CallSign,
                phone = p.Phone,
                isOwn = p.IsOwn,
                email = p.Email,
                note = p.Note,
            }),
            textSnippets = set.TextSnippets.Select(t => new
            {
                shortcut = t.Shortcut,
                text = t.Text,
                category = t.Category,
                etb = t.ForEtb,
                tasks = t.ForTasks,
            }),
            premises = set.Premises.Select(p => new
            {
                name = p.Name,
                street = p.Street,
                houseNumber = p.HouseNumber,
                postalCode = p.PostalCode,
                place = p.Place,
                floors = p.FloorCount,
                apartmentsPerFloor = p.ApartmentsPerFloor,
                undergroundFloors = p.UndergroundFloorCount,
                apartmentLabels = p.ApartmentLabels,
            }),
            settings = new
            {
                ilsReminderIntervalMinutes = set.Settings.IlsReminderIntervalMinutes,
                ilsReminderFollowUpIntervalMinutes = set.Settings.IlsReminderFollowUpIntervalMinutes,
                returnPressureBar = set.Settings.ReturnPressureBar,
                dispatchCentreName = set.Settings.DispatchCentreName,
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
