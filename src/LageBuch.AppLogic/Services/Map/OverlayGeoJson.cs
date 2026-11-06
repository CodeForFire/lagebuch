using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using LageBuch.Domain.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Reads a Kartenebene: a GeoJSON file, or a zip of them, such as a DWD Ausbreitungsprognose.
/// <para>
/// Hostile input. The bytes come from the file picker, from a sync peer, or out of a
/// <c>.lagebuch</c> someone handed over, and every device runs this on them. So every count is
/// capped, every coordinate must lie on the globe, a zip is read in memory under a budget that a
/// zip bomb hits early, no entry name is ever used as a path, and nothing here throws.
/// </para>
/// <para>
/// Features that carry a time (<c>ftime</c>, the DWD's forecast time, or <c>time</c>/<c>timestamp</c>)
/// are grouped into one frame per time; features without one appear in every frame.
/// </para>
/// </summary>
public static class OverlayGeoJson
{
    /// <summary>The most bytes a zip may unpack to, all GeoJSON entries together.</summary>
    public const long MaxUnpackedBytes = 20 * 1024 * 1024;

    /// <summary>The most entries a zip may hold, GeoJSON or not.</summary>
    public const int MaxEntries = 100;

    /// <summary>
    /// The largest central directory a zip may declare: 100 entries with long names fit many times
    /// over, and it bounds what ZipArchive allocates when it reads the directory.
    /// </summary>
    public const long MaxCentralDirectoryBytes = 256 * 1024;

    /// <summary>The most positions read from one Ebene, all shapes and frames together.</summary>
    public const int MaxPoints = 200_000;

    /// <summary>The most shapes read from one Ebene.</summary>
    public const int MaxShapes = 10_000;

    /// <summary>The longest label kept; a longer one is cut.</summary>
    public const int MaxLabelLength = 200;

    public const string InvalidMessage = "Die Datei ist kein gültiges GeoJSON.";
    public const string CrsMessage = "Koordinatensystem nicht unterstützt – bitte als WGS84 (GeoJSON-Standard) exportieren.";
    public const string TooLargeMessage = "Die Datei ist zu groß für eine Kartenebene.";
    public const string TooManyEntriesMessage = "Das Archiv enthält zu viele Dateien.";
    public const string NoGeoJsonInZipMessage = "Das Archiv enthält keine GeoJSON-Datei.";
    public const string ZipFormatMessage = "Das Archiv ist im ZIP64-Format oder auf mehrere Teile gepackt; bitte als gewöhnliches ZIP speichern.";
    public const string EmptyMessage = "Die Datei enthält nichts, was die Lagekarte zeichnen kann.";
    public static readonly string TooManyPointsMessage = $"Die Kartenebene ist zu detailliert (höchstens {MaxPoints:N0} Punkte).";
    public static readonly string TooManyShapesMessage = $"Die Kartenebene hat zu viele Formen (höchstens {MaxShapes:N0}).";
    public static readonly string TooManyFramesMessage = $"Die Kartenebene hat zu viele Zeitpunkte (höchstens {MapOverlay.MaxFrames}).";

    // The colour an Ebene without a style of its own is drawn in. Magenta is the hue the base map
    // never uses, so an unstyled Sperrgebiet cannot be mistaken for a wood or a lake.
    private const uint DefaultRgb = 0xE6007E;
    private const double DefaultFillOpacity = 0.35;
    private const double MinFillOpacity = 0.15;
    private const double MaxFillOpacity = 0.8;
    private const double DefaultStrokeWidth = 2;

    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const int EndOfCentralDirectoryLength = 22;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const int Zip64LocatorLength = 20;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const int CentralHeaderLength = 46;

    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly string[] TimeProperties = ["ftime", "time", "timestamp"];

    /// <summary>
    /// Reads <paramref name="bytes"/> as a Kartenebene, or says in German why it cannot be one.
    /// </summary>
    public static bool TryParse(
        byte[] bytes,
        [NotNullWhen(true)] out OverlayData? data,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        data = null;
        var reader = new Reader();
        try
        {
            error = bytes.AsSpan().StartsWith(ZipMagic) ? reader.ReadZip(bytes) : reader.ReadDocument(bytes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or NotSupportedException)
        {
            // A broken archive or JSON is the file's fault, never the Einsatz's.
            error = InvalidMessage;
        }

        if (error is not null)
        {
            return false;
        }

        return reader.TryBuild(out data, out error);
    }

    // The end-of-central-directory record, found the way ZipArchive finds it: the last signature
    // within a comment's reach of the end. Nothing in it is trusted: the directory it points at is
    // walked here, header by header, the way ZipArchive will walk it, and refused at the first
    // header past the cap -- so ZipArchive never builds more entries than this has counted.
    private static string? CentralDirectoryError(byte[] bytes)
    {
        var lowest = Math.Max(0, bytes.Length - EndOfCentralDirectoryLength - ushort.MaxValue);
        for (var at = bytes.Length - EndOfCentralDirectoryLength; at >= lowest; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) == EndOfCentralDirectorySignature)
            {
                return DirectoryError(bytes, at);
            }
        }

        return InvalidMessage;
    }

    private static string? DirectoryError(byte[] bytes, int at)
    {
        var record = bytes.AsSpan(at);
        var disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

        // Any field at all ones, or a ZIP64 locator in front of the record, sends ZipArchive to read
        // the real numbers from somewhere else. No Ebene needs an archive that size.
        if (disk == ushort.MaxValue || directoryDisk == ushort.MaxValue || entriesOnDisk == ushort.MaxValue
            || entries == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue
            || (at >= Zip64LocatorLength && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at - Zip64LocatorLength)) == Zip64LocatorSignature))
        {
            return ZipFormatMessage;
        }

        // One disk only, and the directory lies whole before the record that describes it.
        if (disk != 0 || directoryDisk != 0 || entriesOnDisk != entries || (long)offset + size > at)
        {
            return InvalidMessage;
        }

        if (entries > MaxEntries || size > MaxCentralDirectoryBytes)
        {
            return TooManyEntriesMessage;
        }

        var position = (long)offset;
        var count = 0;
        while (position + CentralHeaderLength <= at
               && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)position)) == CentralHeaderSignature)
        {
            if (++count > MaxEntries)
            {
                return TooManyEntriesMessage;
            }

            var header = bytes.AsSpan((int)position);
            position += CentralHeaderLength
                + BinaryPrimitives.ReadUInt16LittleEndian(header[28..])
                + BinaryPrimitives.ReadUInt16LittleEndian(header[30..])
                + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
        }

        return count == entries && position == offset + size ? null : InvalidMessage;
    }

    private static bool IsGeoJsonName(string name) =>
        name.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double? Number(JsonElement e, string property)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(property, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDouble(out var d) && double.IsFinite(d) => d,
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) => d,
            _ => null,
        };
    }

    // "#RRGGBB" only; anything else is no colour, so a hostile value cannot reach a colour parser.
    private static uint? Rgb(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return null;
        }

        return uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb) ? rgb : null;
    }

    private static uint Argb(uint rgb, double opacity) =>
        ((uint)Math.Round(Math.Clamp(opacity, 0, 1) * 255) << 24) | (rgb & 0xFFFFFF);

    private static OverlayStyle StyleOf(JsonElement properties, OverlayShapeKind kind)
    {
        var fill = Rgb(Text(properties, "fill"));
        var stroke = Rgb(Text(properties, "stroke"));
        var marker = Rgb(Text(properties, "marker-color"));

        // "fill-opaci" is "fill-opacity" cut to a shapefile's ten-character field name, which is how
        // the DWD's files carry it.
        var fillOpacity = Math.Clamp(
            Number(properties, "fill-opacity") ?? Number(properties, "fill-opaci") ?? DefaultFillOpacity, MinFillOpacity, MaxFillOpacity);
        var strokeOpacity = Math.Clamp(Number(properties, "stroke-opacity") ?? 1, 0.3, 1);
        var strokeWidth = Math.Clamp(Number(properties, "stroke-width") ?? DefaultStrokeWidth, 0.5, 8);

        var main = kind switch
        {
            OverlayShapeKind.Area => fill ?? stroke ?? DefaultRgb,
            OverlayShapeKind.Line => stroke ?? DefaultRgb,
            _ => marker ?? fill ?? DefaultRgb,
        };
        var outline = kind == OverlayShapeKind.Area ? stroke ?? main : main;
        return new OverlayStyle(Argb(main, fillOpacity), Argb(outline, strokeOpacity), strokeWidth);
    }

    private static string? LabelOf(JsonElement properties)
    {
        var label = (Text(properties, "name") ?? Text(properties, "title") ?? Text(properties, "description"))?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        return label[..Math.Min(label.Length, MaxLabelLength)];
    }

    private static DateTimeOffset? TimeOf(JsonElement properties)
    {
        foreach (var name in TimeProperties)
        {
            if (Text(properties, name) is { } text
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
            {
                return time.ToUniversalTime();
            }
        }

        return null;
    }

    // No crs member is WGS84 by the GeoJSON standard; the old 2008 spec's named CRS84 and EPSG:4326
    // say the same. Anything else would put the plume in the wrong place, so it is refused.
    private static bool IsWgs84(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("crs", out var crs) || crs.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        var name = crs.ValueKind == JsonValueKind.Object && crs.TryGetProperty("properties", out var p) ? Text(p, "name") : null;
        return name is not null
            && (name.EndsWith("CRS84", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("EPSG::4326", StringComparison.OrdinalIgnoreCase)
                || name.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Reader
    {
        private readonly List<(DateTimeOffset? Time, OverlayShape Shape)> _shapes = [];
        private int _points;

        public string? ReadZip(byte[] bytes)
        {
            // ZipArchive builds an entry for every record of the central directory the moment it is
            // opened, so the entry cap has to be checked on the bytes before it is: a 25 MB file
            // can declare half a million entries.
            if (CentralDirectoryError(bytes) is { } refused)
            {
                return refused;
            }

            using var stream = new MemoryStream(bytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            // Stryker disable once all : a second line behind CentralDirectoryError, which has counted these headers already.
            if (archive.Entries.Count > MaxEntries)
            {
                return TooManyEntriesMessage;
            }

            long budget = MaxUnpackedBytes;
            var found = false;

            // Entry names only choose which entries to read, and in what order; none is ever a path.
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.FullName.EndsWith('/') || !IsGeoJsonName(entry.Name))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                if (!CopyCapped(entryStream, buffer, budget))
                {
                    return TooLargeMessage;
                }

                budget -= buffer.Length;
                found = true;
                if (ReadDocument(buffer.GetBuffer().AsMemory(0, (int)buffer.Length)) is { } error)
                {
                    return error;
                }
            }

            return found ? null : NoGeoJsonInZipMessage;
        }

        public string? ReadDocument(ReadOnlyMemory<byte> json)
        {
            // A file saved by a Windows tool often starts with a UTF-8 byte order mark, which the
            // JSON reader refuses; it carries nothing, so it is skipped.
            if (json.Span.StartsWith(Utf8Bom))
            {
                json = json[Utf8Bom.Length..];
            }

            // Anything but an object falls through to ReadGeometry below, which refuses it.
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!IsWgs84(root))
            {
                return CrsMessage;
            }

            switch (Text(root, "type"))
            {
                case "FeatureCollection":
                    if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                    {
                        return InvalidMessage;
                    }

                    foreach (var feature in features.EnumerateArray())
                    {
                        if (ReadFeature(feature) is { } error)
                        {
                            return error;
                        }
                    }

                    return null;
                case "Feature":
                    return ReadFeature(root);
                default:
                    return ReadGeometry(root, default, null, depth: 0);
            }
        }

        public bool TryBuild([NotNullWhen(true)] out OverlayData? data, [NotNullWhen(false)] out string? error)
        {
            data = null;
            if (_shapes.Count == 0)
            {
                error = EmptyMessage;
                return false;
            }

            var times = _shapes.Select(s => s.Time).OfType<DateTimeOffset>().Distinct().Order().ToList();
            if (times.Count > MapOverlay.MaxFrames)
            {
                error = TooManyFramesMessage;
                return false;
            }

            var timeless = _shapes.Where(s => s.Time is null).Select(s => s.Shape).ToList();
            List<OverlayFrame> frames = times.Count == 0
                ? [new OverlayFrame(null, timeless)]
                : times
                    .Select(t => new OverlayFrame(t, timeless.Concat(_shapes.Where(s => s.Time == t).Select(s => s.Shape)).ToList()))
                    .ToList();

            var bounds = _shapes.Select(s => s.Shape.Bounds).Aggregate((a, b) => a.Union(b));
            data = new OverlayData(frames, bounds);
            error = null;
            return true;
        }

        private static bool CopyCapped(Stream source, MemoryStream target, long budget)
        {
            var chunk = new byte[81920];
            int n;
            while ((n = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (target.Length + n > budget)
                {
                    return false;
                }

                target.Write(chunk, 0, n);
            }

            return true;
        }

        private string? ReadFeature(JsonElement feature)
        {
            if (feature.ValueKind != JsonValueKind.Object || Text(feature, "type") != "Feature")
            {
                return InvalidMessage;
            }

            // Whatever kind it is: every reader of the properties checks for an object first.
            var properties = feature.TryGetProperty("properties", out var p) ? p : default;

            // A Feature may carry no geometry at all; it has nothing to draw, which is not an error.
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return ReadGeometry(geometry, properties, TimeOf(properties), depth: 0);
        }

        private string? ReadGeometry(JsonElement geometry, JsonElement properties, DateTimeOffset? time, int depth)
        {
            if (geometry.ValueKind != JsonValueKind.Object)
            {
                return InvalidMessage;
            }

            var type = Text(geometry, "type");
            if (type == "GeometryCollection")
            {
                // One level deep, as the spec advises; a nested collection is refused rather than recursed into.
                if (depth > 0 || !geometry.TryGetProperty("geometries", out var geometries) || geometries.ValueKind != JsonValueKind.Array)
                {
                    return InvalidMessage;
                }

                foreach (var child in geometries.EnumerateArray())
                {
                    if (ReadGeometry(child, properties, time, depth + 1) is { } error)
                    {
                        return error;
                    }
                }

                return null;
            }

            if (!geometry.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array)
            {
                return InvalidMessage;
            }

            return type switch
            {
                "Point" => Add(OverlayShapeKind.Point, [c], minimum: 1, properties, time, singlePosition: true),
                "MultiPoint" => Add(OverlayShapeKind.Point, c.EnumerateArray().ToList(), minimum: 1, properties, time, singlePosition: true),
                "LineString" => Add(OverlayShapeKind.Line, [c], minimum: 2, properties, time),
                "MultiLineString" => Add(OverlayShapeKind.Line, c.EnumerateArray().ToList(), minimum: 2, properties, time),
                "Polygon" => Add(OverlayShapeKind.Area, c.EnumerateArray().ToList(), minimum: 3, properties, time),
                "MultiPolygon" => AddMultiPolygon(c, properties, time),
                _ => InvalidMessage,
            };
        }

        private string? AddMultiPolygon(JsonElement coordinates, JsonElement properties, DateTimeOffset? time)
        {
            // All of a MultiPolygon's rings go into one shape: filled even-odd, holes stay holes.
            var rings = new List<JsonElement>();
            foreach (var polygon in coordinates.EnumerateArray())
            {
                if (polygon.ValueKind != JsonValueKind.Array)
                {
                    return InvalidMessage;
                }

                rings.AddRange(polygon.EnumerateArray());
            }

            return Add(OverlayShapeKind.Area, rings, minimum: 3, properties, time);
        }

        private string? Add(
            OverlayShapeKind kind,
            List<JsonElement> parts,
            int minimum,
            JsonElement properties,
            DateTimeOffset? time,
            bool singlePosition = false)
        {
            if (parts.Count == 0)
            {
                return null;
            }

            if (_shapes.Count >= MaxShapes)
            {
                return TooManyShapesMessage;
            }

            var read = new List<IReadOnlyList<GeoPoint>>(parts.Count);
            foreach (var part in parts)
            {
                List<JsonElement>? positions = singlePosition ? [part] : part.ValueKind == JsonValueKind.Array ? part.EnumerateArray().ToList() : null;
                if (positions is null || positions.Count < minimum)
                {
                    return InvalidMessage;
                }

                if (_points + positions.Count > MaxPoints)
                {
                    return TooManyPointsMessage;
                }

                var points = new List<GeoPoint>(positions.Count);
                foreach (var position in positions)
                {
                    if (Position(position) is not { } point)
                    {
                        return InvalidMessage;
                    }

                    points.Add(point);
                }

                _points += points.Count;
                read.Add(points);
            }

            var shape = new OverlayShape(kind, read, StyleOf(properties, kind), LabelOf(properties), GeoBounds.Of(read.SelectMany(p => p)));
            _shapes.Add((time, shape));
            return null;
        }

        // GeoJSON writes longitude first; a third value (height) is ignored.
        private static GeoPoint? Position(JsonElement position) =>
            position.ValueKind == JsonValueKind.Array && position.GetArrayLength() >= 2
            && position[0].ValueKind == JsonValueKind.Number && position[0].TryGetDouble(out var longitude)
            && position[1].ValueKind == JsonValueKind.Number && position[1].TryGetDouble(out var latitude)
                ? GeoPoint.TryCreate(latitude, longitude)
                : null;
    }
}
