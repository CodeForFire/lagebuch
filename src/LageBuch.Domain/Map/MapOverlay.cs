using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Map;

/// <summary>
/// An Ebene on the Lagekarte: a GeoJSON file imported into the Einsatz, such as a DWD
/// Ausbreitungsprognose or a Sperrgebiet drawn elsewhere, or an image laid on the map at its
/// <see cref="Bounds"/>, such as a Luftbild (<see cref="MapOverlayKind.Raster"/>). The geometry
/// or the picture stays in the file
/// (<see cref="FileId"/>, one of the Einsatz's own <see cref="Files.IncidentFile"/>s); this record
/// carries only what the Lagebuch says about the import, so the snapshot every sync pushes stays
/// small however large the plume is.
/// </summary>
public sealed record MapOverlay
{
    /// <summary>The longest name kept; a longer file name is cut, not refused.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The most forecast times one Ebene may carry.</summary>
    public const int MaxFrames = 500;

    /// <summary>The longest licence line kept; a longer one is cut.</summary>
    public const int MaxAttributionLength = 200;

    private MapOverlay(
        Guid id,
        Guid fileId,
        string name,
        DateTimeOffset importedAt,
        string importedBy,
        int frameCount,
        DateTimeOffset? firstTime,
        DateTimeOffset? lastTime,
        MapOverlayKind kind,
        GeoBox? bounds,
        string? attribution)
    {
        Id = id;
        FileId = fileId;
        Name = name;
        ImportedAt = importedAt;
        ImportedBy = importedBy;
        FrameCount = frameCount;
        FirstTime = firstTime;
        LastTime = lastTime;
        Kind = kind;
        Bounds = bounds;
        Attribution = attribution;
    }

    public Guid Id { get; }

    /// <summary>The imported file, as it came in. A reference inside the Einsatz, never into the Stammdaten.</summary>
    public Guid FileId { get; }

    public string Name { get; }

    public DateTimeOffset ImportedAt { get; }

    public string ImportedBy { get; }

    /// <summary>How many points in time the file holds; 1 for a plain Ebene.</summary>
    public int FrameCount { get; }

    /// <summary>The first forecast time, or null when the file carries none.</summary>
    public DateTimeOffset? FirstTime { get; }

    /// <summary>The last forecast time, or null when the file carries none.</summary>
    public DateTimeOffset? LastTime { get; }

    /// <summary>Whether the file holds GeoJSON shapes or one image.</summary>
    public MapOverlayKind Kind { get; }

    /// <summary>
    /// Where an image lies on the map; always set for a <see cref="MapOverlayKind.Raster"/> Ebene and
    /// never for a vector one, whose shapes carry their own positions.
    /// </summary>
    public GeoBox? Bounds { get; }

    /// <summary>
    /// The licence line the source asks for wherever its data is shown, copied at the moment of the
    /// download (see AGENTS.md, Stammdaten in an incident); null when the file needs none.
    /// </summary>
    public string? Attribution { get; }

    /// <summary>True when the Ebene steps through several points in time.</summary>
    public bool IsTimeSeries => FrameCount > 1;

    public static MapOverlay Create(
        Guid id, Guid fileId, MapOverlaySummary summary, DateTimeOffset importedAt, string importedBy)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedBy);
        if (id == Guid.Empty || fileId == Guid.Empty)
        {
            throw new ArgumentException("Kartenebene ohne Kennung.", nameof(id));
        }

        var name = Trimmed(summary.Name)
            ?? throw new ArgumentException("Kartenebene braucht einen Namen.", nameof(summary));
        if (summary.FrameCount is < 1 or > MaxFrames)
        {
            throw new ArgumentException($"Eine Kartenebene hat 1 bis {MaxFrames} Zeitpunkte.", nameof(summary));
        }

        if (summary.FirstTime is null != summary.LastTime is null
            || (summary.FirstTime is { } first && summary.LastTime is { } last && first > last))
        {
            throw new ArgumentException("Die Zeitpunkte der Kartenebene passen nicht zusammen.", nameof(summary));
        }

        if (!Enum.IsDefined(summary.Kind))
        {
            throw new ArgumentException("Unbekannte Art von Kartenebene.", nameof(summary));
        }

        if (summary.Kind == MapOverlayKind.Raster && summary.Bounds is null)
        {
            throw new ArgumentException("Ein Luftbild braucht seine Lage auf der Karte.", nameof(summary));
        }

        if (summary.Kind == MapOverlayKind.Vector && summary.Bounds is not null)
        {
            throw new ArgumentException("Eine GeoJSON-Ebene trägt ihre Lage selbst.", nameof(summary));
        }

        return new MapOverlay(
            id,
            fileId,
            name,
            importedAt,
            importedBy,
            summary.FrameCount,
            summary.FirstTime,
            summary.LastTime,
            summary.Kind,
            summary.Bounds,
            Capped(summary.Attribution, MaxAttributionLength));
    }

    /// <summary>
    /// The load path (SQLite and a host's snapshot). Never throws: a row a foreign build or a hostile
    /// file wrote reads back as the nearest valid Ebene, because an Einsatz that opens with a
    /// slightly wrong layer list beats one that does not open. An image without a box, or of a kind
    /// this build does not know, reads back as a vector Ebene: the map then reports its file as
    /// unreadable rather than placing it anywhere, and it can still be removed.
    /// </summary>
    public static MapOverlay Rehydrate(
        Guid id,
        Guid fileId,
        string? name,
        DateTimeOffset importedAt,
        string? importedBy,
        int frameCount,
        DateTimeOffset? firstTime,
        DateTimeOffset? lastTime,
        MapOverlayKind kind = MapOverlayKind.Vector,
        GeoBox? bounds = null,
        string? attribution = null)
    {
        var timesValid = firstTime is { } first && lastTime is { } last && first <= last;
        var raster = kind == MapOverlayKind.Raster && bounds is not null;
        return new MapOverlay(
            id,
            fileId,
            Trimmed(name) ?? "Kartenebene",
            importedAt,
            importedBy ?? string.Empty,
            Math.Clamp(frameCount, 1, MaxFrames),
            timesValid ? firstTime : null,
            timesValid ? lastTime : null,
            raster ? MapOverlayKind.Raster : MapOverlayKind.Vector,
            raster ? bounds : null,
            Capped(attribution, MaxAttributionLength));
    }

    private static string? Trimmed(string? value) => Capped(value, MaxNameLength);

    private static string? Capped(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed[..Math.Min(trimmed.Length, maxLength)].TrimEnd();
    }
}
