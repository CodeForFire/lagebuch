using System.Globalization;

namespace LageBuch.Domain.CoMeasurement;

public static class CoMeasurementLabels
{
    public static string FloorLabel(int ordinal) => ordinal switch
    {
        0 => "EG",
        > 0 => $"{ordinal}. OG",
        < 0 => $"{-ordinal}. UG", // Untergeschoss (#218): -1 is the first floor below EG
    };

    /// <summary>The building's full floor range, e.g. "EG–3. OG" or, with Untergeschosse,
    /// "2. UG–3. OG" (#218).</summary>
    public static string FloorRangeLabel(int undergroundFloorCount, int floorCount) =>
        undergroundFloorCount > 0
            ? $"{FloorLabel(-undergroundFloorCount)}–{FloorLabel(floorCount)}"
            : $"EG–{FloorLabel(floorCount)}";

    public static string ApartmentLabel(int apartmentNumber) =>
        $"Whg. {apartmentNumber}";

    // Three dwellings per floor is the common walk-up layout, so "links/Mitte/rechts" reads
    // faster on scene than a generic "Whg. N" — still just the default, always user-editable.
    public static string DefaultApartmentLabel(int apartmentNumber, int apartmentsPerFloor) =>
        apartmentsPerFloor == 3
            ? apartmentNumber switch
            {
                1 => "Links",
                2 => "Mitte",
                3 => "Rechts",
                _ => ApartmentLabel(apartmentNumber),
            }
            : ApartmentLabel(apartmentNumber);

    /// <summary>#265: every floor labels its own units independently -- ApartmentLabels is keyed
    /// by <see cref="ApartmentLabelKey"/> (floor + apartment number), not apartment number alone,
    /// so a custom label on one floor never leaks onto a same-numbered unit on another floor.</summary>
    public static string ApartmentLabel(Building building, int floorOrdinal, int apartmentNumber)
    {
        ArgumentNullException.ThrowIfNull(building);
        var key = ApartmentLabelKey(floorOrdinal, apartmentNumber);
        return building.ApartmentLabels.TryGetValue(key, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom!
            : DefaultApartmentLabel(apartmentNumber, building.ApartmentsFor(floorOrdinal));
    }

    public static string ApartmentLabelKey(int floorOrdinal, int apartmentNumber) =>
        $"{floorOrdinal.ToString(CultureInfo.InvariantCulture)}:{apartmentNumber.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Pre-#265 saved incidents keyed ApartmentLabels by apartment number alone (one
    /// label shared across every floor's same-numbered column, e.g. "2" → "Müller"). #265 makes
    /// labels per-floor, so an old label needs to fan out across every floor that existed when it
    /// was saved -- otherwise it silently disappears from a Haus a crew already labeled.</summary>
    public static IReadOnlyDictionary<string, string?> MigrateLegacyApartmentLabels(
        IReadOnlyDictionary<string, string?> raw, int floorCount, int undergroundFloorCount)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var result = new Dictionary<string, string?>();
        foreach (var (key, value) in raw)
        {
            if (key.Contains(':', StringComparison.Ordinal))
            {
                result[key] = value;
                continue;
            }

            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var apartmentNumber))
            {
                continue;
            }

            for (var floor = -undergroundFloorCount; floor <= floorCount; floor++)
            {
                result[ApartmentLabelKey(floor, apartmentNumber)] = value;
            }
        }

        return result;
    }

    public static string DwellingLocation(Building building, int floorOrdinal, int apartmentNumber)
    {
        ArgumentNullException.ThrowIfNull(building);
        return $"{building.Name}, {FloorLabel(floorOrdinal)}, {ApartmentLabel(building, floorOrdinal, apartmentNumber)}";
    }

    public static string StatusText(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "noch nicht abgesucht",
        DwellingStatus.Searched => "abgesucht – keine Personen betroffen",
        DwellingStatus.Affected => "Person(en) betroffen",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string StatusChip(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "GELB",
        DwellingStatus.Searched => "GRÜN",
        DwellingStatus.Affected => "ROT",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
