namespace LageBuch.Domain.CoMeasurement;

public sealed record Building
{
    public Guid Id { get; private init; }

    public string Name { get; private init; } = string.Empty;

    public int FloorCount { get; private init; }

    /// <summary>Untergeschosse below EG (#218), each a negative FloorOrdinal counting down from
    /// -1. Capped at 3 -- deeper basements are rare enough that a free-form count buys nothing
    /// but a bigger typo blast radius.</summary>
    public int UndergroundFloorCount { get; private init; }

    /// <summary>Default Wohnungen-per-floor for a floor with no <see cref="ApartmentCounts"/>
    /// override -- what a newly added floor is seeded with. #265: real buildings' floors,
    /// especially Untergeschosse, rarely share one column count, so this is a starting point, not
    /// a building-wide constraint anymore.</summary>
    public int ApartmentsPerFloor { get; private init; }

    /// <summary>Per-floor overrides of <see cref="ApartmentsPerFloor"/> (#265), keyed by
    /// FloorOrdinal. A floor absent here uses the default.</summary>
    public IReadOnlyDictionary<int, int> ApartmentCounts { get; private init; } =
        new Dictionary<int, int>();

    public IReadOnlyDictionary<int, string?> FloorDescriptions { get; private init; } =
        new Dictionary<int, string?>();

    /// <summary>Custom unit labels (#265), keyed by <see cref="CoMeasurementLabels.ApartmentLabelKey"/>
    /// (FloorOrdinal + ApartmentNumber) -- every floor now labels its own units independently
    /// rather than sharing one column index across the whole building.</summary>
    public IReadOnlyDictionary<string, string?> ApartmentLabels { get; private init; } =
        new Dictionary<string, string?>();

    public int Ordinal { get; private init; }

    private Building()
    {
    }

    public static Building Create(string name, int floorCount, int apartmentsPerFloor, int ordinal, int undergroundFloorCount = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Hausname darf nicht leer sein.", nameof(name));
        }

        if (floorCount < 1 || floorCount > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(floorCount), "Obergeschosse müssen zwischen 1 und 50 liegen.");
        }

        if (apartmentsPerFloor < 1 || apartmentsPerFloor > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(apartmentsPerFloor), "Wohnungen je Geschoss müssen zwischen 1 und 30 liegen.");
        }

        if (undergroundFloorCount < 0 || undergroundFloorCount > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(undergroundFloorCount), "Untergeschosse müssen zwischen 0 und 3 liegen.");
        }

        return new Building
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            FloorCount = floorCount,
            UndergroundFloorCount = undergroundFloorCount,
            ApartmentsPerFloor = apartmentsPerFloor,
            Ordinal = ordinal,
        };
    }

    public static Building Rehydrate(
        Guid id,
        string name,
        int floorCount,
        int apartmentsPerFloor,
        IReadOnlyDictionary<int, string?> floorDescriptions,
        int ordinal,
        IReadOnlyDictionary<string, string?>? apartmentLabels = null,
        int undergroundFloorCount = 0,
        IReadOnlyDictionary<int, int>? apartmentCounts = null)
        => new()
        {
            Id = id,
            Name = name,
            FloorCount = floorCount,
            UndergroundFloorCount = undergroundFloorCount,
            ApartmentsPerFloor = apartmentsPerFloor,
            FloorDescriptions = floorDescriptions,
            ApartmentLabels = CoMeasurementLabels.MigrateLegacyApartmentLabels(
                apartmentLabels ?? new Dictionary<string, string?>(), floorCount, undergroundFloorCount),
            ApartmentCounts = apartmentCounts ?? new Dictionary<int, int>(),
            Ordinal = ordinal,
        };

    public Building WithStructure(int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0)
    {
        if (floorCount < 1 || floorCount > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(floorCount));
        }

        if (apartmentsPerFloor < 1 || apartmentsPerFloor > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(apartmentsPerFloor));
        }

        if (undergroundFloorCount < 0 || undergroundFloorCount > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(undergroundFloorCount));
        }

        return this with
        {
            FloorCount = floorCount,
            ApartmentsPerFloor = apartmentsPerFloor,
            UndergroundFloorCount = undergroundFloorCount,
        };
    }

    public Building WithFloorDescription(int ordinal, string? description)
    {
        var dict = new Dictionary<int, string?>(FloorDescriptions.ToDictionary(kv => kv.Key, kv => kv.Value));
        if (string.IsNullOrWhiteSpace(description))
        {
            dict.Remove(ordinal);
        }
        else
        {
            dict[ordinal] = description.Trim();
        }

        return this with { FloorDescriptions = dict };
    }

    /// <summary>The Wohnungen count for a specific floor (#265): the per-floor override if one was
    /// ever set, otherwise the building's default.</summary>
    public int ApartmentsFor(int floorOrdinal) =>
        ApartmentCounts.TryGetValue(floorOrdinal, out var count) ? count : ApartmentsPerFloor;

    public Building WithApartmentCount(int floorOrdinal, int count)
    {
        if (count < 1 || count > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Wohnungen je Geschoss müssen zwischen 1 und 30 liegen.");
        }

        var dict = new Dictionary<int, int>(ApartmentCounts) { [floorOrdinal] = count };
        return this with { ApartmentCounts = dict };
    }

    public Building WithApartmentLabel(int floorOrdinal, int apartmentNumber, string? label)
    {
        var dict = new Dictionary<string, string?>(ApartmentLabels);
        var key = CoMeasurementLabels.ApartmentLabelKey(floorOrdinal, apartmentNumber);
        if (string.IsNullOrWhiteSpace(label))
        {
            dict.Remove(key);
        }
        else
        {
            dict[key] = label.Trim();
        }

        return this with { ApartmentLabels = dict };
    }
}
