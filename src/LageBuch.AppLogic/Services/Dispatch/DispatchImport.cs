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
    /// </summary>
    public static Vehicle? Match(string requested, IEnumerable<Vehicle> vehicles)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(vehicles);

        // A blank name would equal a vehicle with a blank Funkrufname, and nothing else.
        var name = Normalize(requested);
        if (name.Length == 0)
        {
            return null;
        }

        var candidates = vehicles.ToList();
        if (candidates.FirstOrDefault(v => string.Equals(Normalize(v.CallSign), name, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact;
        }

        var partial = candidates
            .Where(v => EndsWithWord(name, Normalize(v.CallSign)) || EndsWithWord(Normalize(v.CallSign), name))
            .DistinctBy(v => Normalize(v.CallSign), StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        foreach (var unit in Units)
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

    private static bool EndsWithWord(string longer, string shorter) =>
        longer.EndsWith(" " + shorter, StringComparison.OrdinalIgnoreCase);
}
