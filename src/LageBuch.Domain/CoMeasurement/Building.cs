namespace LageBuch.Domain.CoMeasurement;

public sealed record Building
{
    public Guid Id { get; private init; }

    public string Name { get; private init; } = string.Empty;

    public int FloorCount { get; private init; }

    public int ApartmentsPerFloor { get; private init; }

    public IReadOnlyDictionary<int, string?> FloorDescriptions { get; private init; } =
        new Dictionary<int, string?>();

    public IReadOnlyDictionary<int, string?> ApartmentLabels { get; private init; } =
        new Dictionary<int, string?>();

    public int Ordinal { get; private init; }

    private Building()
    {
    }

    public static Building Create(string name, int floorCount, int apartmentsPerFloor, int ordinal)
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

        return new Building
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            FloorCount = floorCount,
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
        IReadOnlyDictionary<int, string?>? apartmentLabels = null)
        => new()
        {
            Id = id,
            Name = name,
            FloorCount = floorCount,
            ApartmentsPerFloor = apartmentsPerFloor,
            FloorDescriptions = floorDescriptions,
            ApartmentLabels = apartmentLabels ?? new Dictionary<int, string?>(),
            Ordinal = ordinal,
        };

    public Building WithStructure(int floorCount, int apartmentsPerFloor)
    {
        if (floorCount < 1 || floorCount > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(floorCount));
        }

        if (apartmentsPerFloor < 1 || apartmentsPerFloor > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(apartmentsPerFloor));
        }

        return this with { FloorCount = floorCount, ApartmentsPerFloor = apartmentsPerFloor };
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

    public Building WithApartmentLabel(int apartmentNumber, string? label)
    {
        var dict = new Dictionary<int, string?>(ApartmentLabels.ToDictionary(kv => kv.Key, kv => kv.Value));
        if (string.IsNullOrWhiteSpace(label))
        {
            dict.Remove(apartmentNumber);
        }
        else
        {
            dict[apartmentNumber] = label.Trim();
        }

        return this with { ApartmentLabels = dict };
    }
}