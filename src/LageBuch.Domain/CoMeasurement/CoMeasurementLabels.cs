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

    public static string ApartmentLabel(Building building, int apartmentNumber)
    {
        ArgumentNullException.ThrowIfNull(building);
        return building.ApartmentLabels.TryGetValue(apartmentNumber, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom!
            : DefaultApartmentLabel(apartmentNumber, building.ApartmentsPerFloor);
    }

    public static string DwellingLocation(Building building, int floorOrdinal, int apartmentNumber)
    {
        ArgumentNullException.ThrowIfNull(building);

        // UG units (#265) are a free-form list, not a shared grid column: ApartmentLabels is
        // keyed only by apartment number and shared across every EG/OG floor, so reusing it here
        // would leak an unrelated OG column's custom label (e.g. "Müller") onto a same-numbered
        // UG unit that has nothing to do with it.
        var unit = floorOrdinal < 0 ? $"Einheit {apartmentNumber}" : ApartmentLabel(building, apartmentNumber);
        return $"{building.Name}, {FloorLabel(floorOrdinal)}, {unit}";
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