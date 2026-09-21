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

    /// <summary>Whether a crew ever recorded anything here — what makes deleting this Wohnung
    /// worth asking about rather than doing silently (#419). <see cref="Readings"/> is checked in
    /// its own right and not folded into <see cref="CoValue"/>: clearing a measurement is itself
    /// recorded as a reading (see <see cref="CoReading"/>), so a corrected Wohnung has a null
    /// value and a real history behind it.</summary>
    public bool HasData =>
        ResidentName is not null
        || Status != DwellingStatus.NotSearched
        || KeyAvailable is not null
        || CoValue is not null
        || Readings.Count > 0;

    public Dwelling WithStatus(DwellingStatus status) => this with { Status = status };

    /// <summary>Renumbers this Wohnung within its floor (#419). A <c>with</c>-expression rather
    /// than a fresh <see cref="Create"/> on purpose: <see cref="Id"/> is what the persisted
    /// <c>co_readings</c> rows hang off, so recreating the record would orphan the Messreihe.
    /// </summary>
    public Dwelling WithApartmentNumber(int apartmentNumber) =>
        this with { ApartmentNumber = apartmentNumber };

    public Dwelling WithDetails(string? residentName, bool? keyAvailable) => this with
    {
        ResidentName = string.IsNullOrWhiteSpace(residentName) ? null : residentName.Trim(),
        KeyAvailable = keyAvailable,
    };
}