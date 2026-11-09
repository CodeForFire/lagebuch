using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// An alarm the Lagebuchführer chose to take up, with the Einsatzmittel ticked in the prompt, and
/// how it goes into the new incident. Every value is copied in; the incident keeps no reference to
/// the alarm or to the Stammdaten (#337).
/// </summary>
public sealed record DispatchImport(DispatchAlarm Alarm, IReadOnlyList<DispatchUnit> Units)
{
    /// <summary>Who an alarm entry in the ETB comes from.</summary>
    public const string JournalSender = "Leitstelle";

    /// <summary>
    /// The Stammdaten vehicle a requested Funkrufname names: the same name, or one ending in the
    /// other after a space, so "2/40/1" and "Florian Musterstadt 2/40/1" find each other. Null
    /// when nothing matches, or when the name is only a suffix of more than one vehicle -- a wrong
    /// tick is worse than none.
    /// <para>
    /// A place the Funkrufnamen abbreviate is first written the Stammdaten way, as whole words,
    /// longest place first: with "Musterstadt" → "MST", "Florian Musterstadt 2/40/1" finds
    /// "MST 2/40/1". The name as alarmed still counts too, so a vehicle kept under its full
    /// Funkrufname is found as before. The numbers alone never decide; many an Ort has a 40/1.
    /// </para>
    /// </summary>
    public static Vehicle? Match(string requested, IEnumerable<Vehicle> vehicles, IReadOnlyList<DispatchPlaceName>? placeNames = null)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(vehicles);

        // A blank name would equal a vehicle with a blank Funkrufname, and nothing else.
        var written = Normalize(requested);
        if (written.Length == 0)
        {
            return null;
        }

        // The alarm's own name and its abbreviated form both count, so a vehicle kept under its
        // full Funkrufname still matches once a place name is entered. The two may name two
        // vehicles; then it is as ambiguous as any other pair.
        string[] names = [written, Abbreviate(written, placeNames ?? [])];
        var candidates = vehicles.ToList();
        var exact = ByCallSign(candidates.Where(v => names.Any(n => string.Equals(Normalize(v.CallSign), n, StringComparison.OrdinalIgnoreCase))));
        if (exact.Count > 0)
        {
            return exact is [var one] ? one : null;
        }

        var partial = ByCallSign(candidates.Where(v => names.Any(n => EndsWithWord(n, Normalize(v.CallSign)) || EndsWithWord(Normalize(v.CallSign), n))));
        return partial is [var only] ? only : null;
    }

    /// <summary>"20261108-1717 THL 2 T2917.lagebuch": the alarm time and Stichwort, made safe as a file name.</summary>
    public string SuggestedFileName(DateTimeOffset now)
    {
        var at = (Alarm.AlarmedAt ?? now).ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);

        // A slash is common in a Stichwort ("B 3/RD"); as a path separator it would cut the name
        // down to what follows it, so it becomes a dash before the name is made safe.
        var name = Alarm.Keyword is { } keyword ? $"{at} {keyword.Replace('/', '-').Replace('\\', '-')}" : at;
        return SafeFileName.Sanitize(name, at) + IncidentFileExtension.Current;
    }

    /// <summary>
    /// The first entry of the ETB: what the Leitstelle alarmed for, and when. Written at the moment
    /// the incident is created, like every entry, and naming the alarm time in its text.
    /// </summary>
    public string JournalText()
    {
        var head = Alarm.AlarmedAt is { } at ? $"Alarmierung {Formatting.TimeOfDay(at)}" : "Alarmierung";
        if (Alarm.IsTest)
        {
            head += " (Probealarm)";
        }

        var parts = new List<string>();
        var what = string.Join(" – ", new[] { Alarm.IncidentType ?? Alarm.Keyword, Alarm.Report }.OfType<string>());
        if (what.Length > 0)
        {
            parts.Add(what);
        }

        if (Alarm.Hint is { } hint)
        {
            parts.Add($"Hinweis: {hint}");
        }

        if (Alarm.Building is { } building)
        {
            parts.Add($"Objekt: {building}");
        }

        if (Alarm.RequestedUnits.Count > 0)
        {
            parts.Add($"Alarmiert: {string.Join(", ", Alarm.RequestedUnits)}");
        }

        return parts.Count == 0 ? head : $"{head}: {string.Join(". ", parts)}";
    }

    /// <summary>
    /// Writes the alarm into a freshly started incident: the address, the Einsatzort, the alarm
    /// entry and the ticked Einsatzmittel. A street the Stammdaten know fills in Ortsteil, PLZ and
    /// Ort where all of its rows agree, as the Einsatzdaten dialog does (#461); otherwise the
    /// alarm's own PLZ and Ort stand.
    /// </summary>
    public void ApplyTo(IIncidentSession session, IReadOnlyList<Street> streets)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(streets);

        var rows = Alarm.Street is { } street
            ? streets.Where(s => string.Equals(s.Name.Trim(), street, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        session.SetAddress(
            rows.Count > 0 ? rows[0].Name.Trim() : Alarm.Street,
            Agreed(rows, s => s.District),
            Alarm.HouseNumber,
            Agreed(rows, s => s.PostalCode) ?? Alarm.PostalCode,
            Agreed(rows, s => s.Place) ?? Alarm.Place);

        if (Alarm.Location is { } location)
        {
            session.SetIncidentLocation(location);
        }

        session.AddJournalEntry(EtbDirection.Incoming, JournalText(), from: JournalSender);

        // One unit ticked under two names ("2/40/1" and its full Funkrufname, or another case)
        // is still one unit; recording it twice would double the Stärke.
        foreach (var unit in Units.DistinctBy(u => u.Vehicle?.CallSign ?? u.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (unit.Vehicle is { } vehicle)
            {
                var preset = VehiclePreset.Of(vehicle);
                session.AddForceUnit(vehicle.Wache, preset.Strength, vehicle.CallSign, officerCount: preset.Officers, zugfuehrerCount: preset.Zugfuehrer, vehicleType: preset.VehicleType);
            }
            else
            {
                // Not in the Stammdaten: the Funkrufname as alarmed, Stärke still to be reported.
                session.AddForceUnit(Alarm.Organisation ?? unit.Name, 0, unit.Name);
            }
        }
    }

    // The one non-blank value every row holds, or null when a row is blank or two rows differ.
    private static string? Agreed(List<Street> rows, Func<Street, string> field)
    {
        var values = rows.Select(r => field(r).Trim()).Distinct(StringComparer.Ordinal).ToList();
        return values is [{ Length: > 0 } only] ? only : null;
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    // Every whole-word occurrence of a place name, in any case, becomes its short form, in one
    // pass over the alarm's own words: at each word the place with the most words wins, so
    // "Musterstadt Nord" is not cut down to "MST Nord". A short form is never read again. The
    // place names come from Stammdaten an import or a sync peer may have written, and rewriting
    // the output of one entry with the next ("a" -> "b b", "b" -> "c c", ...) would grow the
    // name exponentially; one pass keeps it to the alarm's words times the longest short form.
    private static string Abbreviate(string name, IReadOnlyList<DispatchPlaceName> placeNames)
    {
        var places = placeNames
            .Select(p => (Words: Normalize(p.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries), Abbreviation: Normalize(p.Abbreviation)))

            // An empty place would match at every word without moving past it.
            .Where(p => p.Words.Length > 0 && p.Abbreviation.Length > 0)
            .OrderByDescending(p => p.Words.Length)
            .ToList();
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(words.Length);
        var at = 0;
        while (at < words.Length)
        {
            var place = places.Find(p => StartsAt(words, at, p.Words));
            if (place.Words is null)
            {
                result.Add(words[at]);
                at++;
            }
            else
            {
                result.Add(place.Abbreviation);
                at += place.Words.Length;
            }
        }

        return string.Join(' ', result);
    }

    private static bool StartsAt(string[] words, int at, string[] place)
    {
        if (at + place.Length > words.Length)
        {
            return false;
        }

        for (var i = 0; i < place.Length; i++)
        {
            if (!string.Equals(words[at + i], place[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // One vehicle per Funkrufname: the same one listed twice is not a choice between two.
    private static List<Vehicle> ByCallSign(IEnumerable<Vehicle> vehicles) =>
        vehicles.DistinctBy(v => Normalize(v.CallSign), StringComparer.OrdinalIgnoreCase).ToList();

    private static bool EndsWithWord(string longer, string shorter) =>
        longer.EndsWith(" " + shorter, StringComparison.OrdinalIgnoreCase);
}
