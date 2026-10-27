namespace LageBuch.Domain.CoMeasurement;

public sealed record Dwelling
{
    /// <summary>The longest Schlüsselnummer accepted, after trimming. A key tag reads "14", "B-3" or
    /// "Hof 2"; the cap is there because the value also arrives from a sync peer.</summary>
    public const int MaxKeyNumberLength = 20;

    public Guid Id { get; private init; }

    public Guid BuildingId { get; private init; }

    public int FloorOrdinal { get; private init; }

    public int ApartmentNumber { get; private init; }

    public string? ResidentName { get; private init; }

    public DwellingStatus Status { get; private init; }

    public KeyAvailability KeyAvailable { get; private init; }

    /// <summary>The tag or Schlüsselbrett number of a handed-over key, so it can be given back.
    /// Only ever set while <see cref="KeyAvailable"/> is <see cref="KeyAvailability.Available"/>.</summary>
    public string? KeyNumber { get; private init; }

    /// <summary>The current value of each gas measured here, keyed by the incident's own gas id
    /// (#410). A gas without a key has no value: never measured, or cleared.</summary>
    public IReadOnlyDictionary<Guid, decimal> Values { get; private init; } = EmptyValues;

    /// <summary>Every reading taken here, oldest first; per gas, the last one is that gas's entry
    /// in <see cref="Values"/>. Empty for a Wohnung measured before #424 — no history is invented
    /// for old files.</summary>
    public IReadOnlyList<CoReading> Readings { get; private init; } = Array.Empty<CoReading>();

    private static readonly IReadOnlyDictionary<Guid, decimal> EmptyValues = new Dictionary<Guid, decimal>();

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
    /// <paramref name="keyNumber"/> follows it for the same reason.
    /// </summary>
    public static Dwelling Rehydrate(
        Guid id,
        Guid buildingId,
        int floorOrdinal,
        int apartmentNumber,
        string? residentName,
        DwellingStatus status,
        KeyAvailability keyAvailable,
        IReadOnlyDictionary<Guid, decimal>? values,
        IEnumerable<CoReading>? readings = null,
        string? keyNumber = null)
        => new()
        {
            Id = id,
            BuildingId = buildingId,
            FloorOrdinal = floorOrdinal,
            ApartmentNumber = apartmentNumber,
            ResidentName = residentName,
            Status = status,
            KeyAvailable = keyAvailable,
            KeyNumber = keyNumber,
            Values = values is null ? EmptyValues : new Dictionary<Guid, decimal>(values),
            Readings = (readings ?? Enumerable.Empty<CoReading>()).ToList(),
        };

    /// <summary>This Wohnung with every value and reading dropped whose gas is not among
    /// <paramref name="gasIds"/> -- what a file or snapshot naming a gas its own list lacks gets on
    /// load, so nothing renders against a gas nobody defined.</summary>
    public Dwelling KeepingGases(IReadOnlySet<Guid> gasIds)
    {
        ArgumentNullException.ThrowIfNull(gasIds);
        if (Values.Keys.All(gasIds.Contains) && Readings.All(r => gasIds.Contains(r.GasId)))
        {
            return this;
        }

        return this with
        {
            Values = Values.Where(kv => gasIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Readings = Readings.Where(r => gasIds.Contains(r.GasId)).ToList(),
        };
    }

    /// <summary>The current value of one gas, or null when it has none.</summary>
    public decimal? ValueOf(Guid gasId) => Values.TryGetValue(gasId, out var value) ? value : null;

    /// <summary>
    /// Records readings: moves each gas's entry in <see cref="Values"/> and appends one reading per
    /// gas to <see cref="Readings"/> as one operation, all with the same time, so a value cannot
    /// move without leaving a trace — the bug class behind #424. Callers pass only gases that
    /// changed (see <c>Incident.RecordCoValues</c>), which is also what keeps a retried sync command
    /// from inflating the series.
    /// </summary>
    public Dwelling WithCoReadings(IEnumerable<KeyValuePair<Guid, decimal?>> changes, DateTimeOffset measuredAt, string recordedBy)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var values = new Dictionary<Guid, decimal>(Values);
        var readings = new List<CoReading>(Readings);
        foreach (var (gasId, value) in changes)
        {
            if (value is { } v)
            {
                values[gasId] = v;
            }
            else
            {
                values.Remove(gasId);
            }

            readings.Add(new CoReading(measuredAt, gasId, value, recordedBy));
        }

        return this with { Values = values, Readings = readings };
    }

    /// <summary>Whether a crew ever recorded anything here — what makes deleting this Wohnung
    /// worth asking about rather than doing silently (#419). <see cref="Readings"/> is checked in
    /// its own right and not folded into <see cref="Values"/>: clearing a measurement is itself
    /// recorded as a reading (see <see cref="CoReading"/>), so a corrected Wohnung has a null
    /// value and a real history behind it.</summary>
    public bool HasData =>
        ResidentName is not null
        || Status != DwellingStatus.NotSearched
        || KeyAvailable != KeyAvailability.Unknown
        || Values.Count > 0
        || Readings.Count > 0;

    public Dwelling WithStatus(DwellingStatus status) => this with { Status = status };

    /// <summary>Renumbers this Wohnung within its floor (#419). A <c>with</c>-expression rather
    /// than a fresh <see cref="Create"/> on purpose: <see cref="Id"/> is what the persisted
    /// <c>co_readings</c> rows hang off, so recreating the record would orphan the Messreihe.
    /// </summary>
    public Dwelling WithApartmentNumber(int apartmentNumber) =>
        this with { ApartmentNumber = apartmentNumber };

    /// <summary>Sets the Bewohner and the Schlüssel. The <paramref name="keyNumber"/> is kept only
    /// while the key is there: once it is "Keiner" or "Unbekannt" there is no key to give back.
    /// </summary>
    public Dwelling WithDetails(string? residentName, KeyAvailability keyAvailable, string? keyNumber = null) => this with
    {
        ResidentName = string.IsNullOrWhiteSpace(residentName) ? null : residentName.Trim(),
        KeyAvailable = keyAvailable,
        KeyNumber = keyAvailable == KeyAvailability.Available && !string.IsNullOrWhiteSpace(keyNumber) ? keyNumber.Trim() : null,
    };
}