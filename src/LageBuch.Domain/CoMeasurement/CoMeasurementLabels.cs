namespace LageBuch.Domain.CoMeasurement;

public static class CoMeasurementLabels
{
    public static string FloorLabel(int ordinal) =>
        ordinal == 0 ? "EG" : $"{ordinal}. OG";

    public static string ApartmentLabel(int apartmentNumber) =>
        $"Whg. {apartmentNumber}";

    public static string DwellingLocation(Building building, int floorOrdinal, int apartmentNumber) =>
        $"{building.Name}, {FloorLabel(floorOrdinal)}, {ApartmentLabel(apartmentNumber)}";

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