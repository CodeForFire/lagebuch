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

    /// <summary>Every reading taken here, oldest first; the last one is <see cref="CoValue"/>.
    /// Empty for a Wohnung measured before #424 — no history is invented for old files.</summary>
    public IReadOnlyList<CoReading> Readings { get; private init; } = Array.Empty<CoReading>();

    public static Dwelling Create(Guid buildingId, int floorOrdinal, int apartmentNumber)
        => new()
        {
            Id = Guid.NewGuid(),
            BuildingId = buildingId,
            FloorOrdinal = floorOrdinal,
            ApartmentNumber = apartmentNumber,
            Status = DwellingStatus.NotSearched,
        };

    /// <summary>
    /// Rebuilds a Wohnung from storage. <paramref name="readings"/> is appended last with a
    /// default, the same convention <see cref="LageBuch.Domain.ForceUnit.Rehydrate"/> uses for its
    /// edit history, so a file or snapshot written before #424 rehydrates with an empty series.
    /// </summary>
    public static Dwelling Rehydrate(
        Guid id,
        Guid buildingId,
        int floorOrdinal,
        int apartmentNumber,
        string? residentName,
        DwellingStatus status,
        bool? keyAvailable,
        int? coValue,
        IEnumerable<CoReading>? readings = null)
        => new()
        {
            Id = id,
            BuildingId = buildingId,
            FloorOrdinal = floorOrdinal,
            ApartmentNumber = apartmentNumber,
            ResidentName = residentName,
            Status = status,
            KeyAvailable = keyAvailable,
            CoValue = coValue,
            Readings = (readings ?? Enumerable.Empty<CoReading>()).ToList(),
        };

    /// <summary>
    /// Records a reading: moves <see cref="CoValue"/> and appends to <see cref="Readings"/> as one
    /// operation, so the value cannot move without leaving a trace — the bug class behind #424.
    /// Callers guard against no-op writes themselves (see <c>Incident.RecordCoValue</c>), which is
    /// also what keeps a retried sync command from inflating the series.
    /// </summary>
    public Dwelling WithCoReading(int? coValue, DateTimeOffset measuredAt, string recordedBy) => this with
    {
        CoValue = coValue,
        Readings = new List<CoReading>(Readings) { new(measuredAt, coValue, recordedBy) },
    };

    public Dwelling WithStatus(DwellingStatus status) => this with { Status = status };

    public Dwelling WithDetails(string? residentName, bool? keyAvailable) => this with
    {
        ResidentName = string.IsNullOrWhiteSpace(residentName) ? null : residentName.Trim(),
        KeyAvailable = keyAvailable,
    };
}