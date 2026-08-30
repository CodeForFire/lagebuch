namespace LageBuch.Domain.CoMeasurement;

public static class CoMeasurementLabels
{
    public static string FloorLabel(int ordinal) =>
        ordinal == 0 ? "EG" : $"{ordinal}. OG";

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
                _ => ApartmentLabel(apartmentNumber)
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
        return $"{building.Name}, {FloorLabel(floorOrdinal)}, {ApartmentLabel(building, apartmentNumber)}";
    }

    public static string StatusText(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "noch nicht abgesucht",
        DwellingStatus.Searched => "abgesucht – keine Personen betroffen",
        DwellingStatus.Affected => "Person(en) betroffen",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static string StatusChip(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "GELB",
        DwellingStatus.Searched => "GRÜN",
        DwellingStatus.Affected => "ROT",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}