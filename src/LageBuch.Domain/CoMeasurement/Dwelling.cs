namespace LageBuch.Domain.CoMeasurement;

public sealed record Dwelling
{
    public Guid Id { get; private init; }
    public Guid BuildingId { get; private init; }
    public int FloorOrdinal { get; private init; }
    public int ApartmentNumber { get; private init; }
    public string? ResidentName { get; private init; }
    public DwellingStatus Status { get; private init; }
    public bool? KeyAvailable { get; private init; }
    public int? CoValue { get; private init; }

    public static Dwelling Create(Guid buildingId, int floorOrdinal, int apartmentNumber)
        => new()
        {
            Id = Guid.NewGuid(),
            BuildingId = buildingId,
            FloorOrdinal = floorOrdinal,
            ApartmentNumber = apartmentNumber,
            Status = DwellingStatus.NotSearched
        };

    public static Dwelling Rehydrate(
        Guid id, Guid buildingId, int floorOrdinal, int apartmentNumber,
        string? residentName, DwellingStatus status, bool? keyAvailable, int? coValue)
        => new()
        {
            Id = id,
            BuildingId = buildingId,
            FloorOrdinal = floorOrdinal,
            ApartmentNumber = apartmentNumber,
            ResidentName = residentName,
            Status = status,
            KeyAvailable = keyAvailable,
            CoValue = coValue
        };

    public Dwelling WithCoValue(int? coValue) => this with { CoValue = coValue };

    public Dwelling WithStatus(DwellingStatus status) => this with { Status = status };

    public Dwelling WithDetails(string? residentName, bool? keyAvailable) => this with
    {
        ResidentName = string.IsNullOrWhiteSpace(residentName) ? null : residentName.Trim(),
        KeyAvailable = keyAvailable
    };
}