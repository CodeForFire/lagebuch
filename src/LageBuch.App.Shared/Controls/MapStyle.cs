using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Every colour the Lagekarte paints a Kartenpaket in: the land, the roads, the names and the
/// water points' labels. <see cref="Day"/> is OSM Carto's look, the one a crew knows from
/// openstreetmap.org; <see cref="Night"/> the same map dark, for an ELW at night, where the day
/// map is the brightest thing on the screen. <see cref="VectorTileRenderer"/> and
/// <see cref="MapDrawing"/> draw the same features in either; only the colours differ.
/// </summary>
/// <remarks>
/// The night colours keep the day map's order of brightness between road classes, so the
/// Hauptstraßen stand out as they do by day, and draw them in amber over a dark grey, the way a
/// car's navigation does at night. Water is lifted clear of forest, and wetland given a teal of its
/// own, so lake, Moos and Wald stay three things in the dark. The POI inks are lighter versions of
/// the day's, so the key's meanings carry over. A taktisches Zeichen is not part of the map and
/// keeps its colours in both.
/// </remarks>
public sealed class MapStyle
{
    public static readonly MapStyle Day = new()
    {
        Background = Solid(0xE5E3DF),
        Earth = Solid(0xF2EFE9),
        Water = Solid(0xAAD3DF),
        Building = Solid(0xD9D0C9),
        BuildingOutline = Solid(0xC4B6AB),
        Boundary = Solid(0xAC46AC),
        LabelInk = Solid(0x333333),
        HouseNumberInk = Solid(0x666666),
        Halo = Solid(0xFFFFFF),
        WaterLabelInk = Solid(0x4D80B3),
        OneWayInk = Solid(0x6E6E6E),
        FireStationInk = Solid(0xE65100),
        HelpInk = Solid(0x6A1B9A),
        PeopleInk = Solid(0xAD1457),
        OfficeInk = Solid(0x455A64),
        FuelInk = Solid(0x795548),
        Footway = Solid(0xFA8072),
        Cycleway = Solid(0x0000FF),
        Track = Solid(0x996600),
        Rail = Solid(0x707070),
        SelectedRing = Solid(0x212121),
        Landuse = new Dictionary<string, IImmutableBrush>(StringComparer.Ordinal)
        {
            ["residential"] = Solid(0xE0DFDF),
            ["industrial"] = Solid(0xEBDBE8),
            ["commercial"] = Solid(0xF2DAD9),
            ["retail"] = Solid(0xFFD6D1),
            ["farmland"] = Solid(0xEEF0D5),
            ["farmyard"] = Solid(0xF5DCBA),
            ["forest"] = Solid(0xADD19E),
            ["wood"] = Solid(0xADD19E),
            ["grass"] = Solid(0xCDEBB0),
            ["meadow"] = Solid(0xCDEBB0),
            ["park"] = Solid(0xC8FACC),
            ["garden"] = Solid(0xC8FACC),
            ["playground"] = Solid(0xDFFCE2),
            ["pitch"] = Solid(0x88E0BE),
            ["cemetery"] = Solid(0xAACBAF),
            ["allotments"] = Solid(0xC9E1BF),
            ["school"] = Solid(0xFFFFE5),
            ["hospital"] = Solid(0xFFFFE5),
            ["pedestrian"] = Solid(0xDDDDE8),
            ["railway"] = Solid(0xEBDBE8),
            ["military"] = Solid(0xF2E4E4),
            ["wetland"] = Solid(0xD6EBE0),
        },
        Roads = new Dictionary<string, RoadColours>(StringComparer.Ordinal)
        {
            ["motorway"] = new(Solid(0xE892A2), Solid(0xDC2A67)),
            ["trunk"] = new(Solid(0xF9B29C), Solid(0xC84E2F)),
            ["primary"] = new(Solid(0xFCD6A4), Solid(0xA06B00)),
            ["secondary"] = new(Solid(0xF7FABF), Solid(0x707D05)),
            ["tertiary"] = new(Solid(0xFFFFFF), Solid(0x8F8F8F)),
            ["unclassified"] = new(Solid(0xFFFFFF), Solid(0xBBBBBB)),
            ["residential"] = new(Solid(0xFFFFFF), Solid(0xBBBBBB)),
            ["living_street"] = new(Solid(0xEDEDED), Solid(0xBBBBBB)),
            ["service"] = new(Solid(0xFFFFFF), Solid(0xBBBBBB)),
            ["pedestrian"] = new(Solid(0xDDDDE8), Solid(0x999999)),
        },
    };

    public static readonly MapStyle Night = new()
    {
        Background = Solid(0x141518),
        Earth = Solid(0x1C1E21),
        Water = Solid(0x2A5878),
        Building = Solid(0x303237),
        BuildingOutline = Solid(0x3E4046),
        Boundary = Solid(0x8E5A8E),
        LabelInk = Solid(0xE4E4E4),
        HouseNumberInk = Solid(0xA8A8A8),
        Halo = Solid(0x141518),
        WaterLabelInk = Solid(0x8EC2E8),
        OneWayInk = Solid(0x8A8A8A),
        FireStationInk = Solid(0xFF8A3D),
        HelpInk = Solid(0xB585DD),
        PeopleInk = Solid(0xE8709F),
        OfficeInk = Solid(0x9AAEB8),
        FuelInk = Solid(0xB8968A),
        Footway = Solid(0x8A4A44),
        Cycleway = Solid(0x5070E0),
        Track = Solid(0x7A5A1A),
        Rail = Solid(0x8C8C8C),
        SelectedRing = Solid(0xE4E4E4),
        Landuse = new Dictionary<string, IImmutableBrush>(StringComparer.Ordinal)
        {
            ["residential"] = Solid(0x25272A),
            ["industrial"] = Solid(0x2C272E),
            ["commercial"] = Solid(0x2E2526),
            ["retail"] = Solid(0x382729),
            ["farmland"] = Solid(0x212520),
            ["farmyard"] = Solid(0x2C2921),
            ["forest"] = Solid(0x1B2D1F),
            ["wood"] = Solid(0x1B2D1F),
            ["grass"] = Solid(0x20301F),
            ["meadow"] = Solid(0x20301F),
            ["park"] = Solid(0x1E3323),
            ["garden"] = Solid(0x1E3323),
            ["playground"] = Solid(0x233228),
            ["pitch"] = Solid(0x1E4034),
            ["cemetery"] = Solid(0x23332A),
            ["allotments"] = Solid(0x24301F),
            ["school"] = Solid(0x302F22),
            ["hospital"] = Solid(0x302F22),
            ["pedestrian"] = Solid(0x383843),
            ["railway"] = Solid(0x2C272E),
            ["military"] = Solid(0x2D2424),
            ["wetland"] = Solid(0x24403F),
        },
        Roads = new Dictionary<string, RoadColours>(StringComparer.Ordinal)
        {
            ["motorway"] = new(Solid(0xB8673A), Solid(0xE08A4A)),
            ["trunk"] = new(Solid(0xA8703C), Solid(0xD09050)),
            ["primary"] = new(Solid(0x9A7E3C), Solid(0xC8A650)),
            ["secondary"] = new(Solid(0x6E6A40), Solid(0x9A9455)),
            ["tertiary"] = new(Solid(0x484B51), Solid(0x6A6D73)),
            ["unclassified"] = new(Solid(0x484B51), Solid(0x393B40)),
            ["residential"] = new(Solid(0x484B51), Solid(0x393B40)),
            ["living_street"] = new(Solid(0x414348), Solid(0x393B40)),
            ["service"] = new(Solid(0x484B51), Solid(0x393B40)),
            ["pedestrian"] = new(Solid(0x383843), Solid(0x60606A)),
        },
    };

    private MapStyle()
    {
    }

    /// <summary>Behind the tiles: a shade darker than the land, so the gap past a pack's edge reads as "no map here", not as an error.</summary>
    public required IImmutableBrush Background { get; init; }

    public required IImmutableBrush Earth { get; init; }

    public required IImmutableBrush Water { get; init; }

    public required IImmutableBrush Building { get; init; }

    public required IImmutableBrush BuildingOutline { get; init; }

    public required IImmutableBrush Boundary { get; init; }

    public required IImmutableBrush LabelInk { get; init; }

    public required IImmutableBrush HouseNumberInk { get; init; }

    /// <summary>Around every name but a house number, in the colour of the land beneath it.</summary>
    public required IImmutableBrush Halo { get; init; }

    public required IImmutableBrush WaterLabelInk { get; init; }

    public required IImmutableBrush OneWayInk { get; init; }

    public required IImmutableBrush FireStationInk { get; init; }

    public required IImmutableBrush HelpInk { get; init; }

    public required IImmutableBrush PeopleInk { get; init; }

    public required IImmutableBrush OfficeInk { get; init; }

    public required IImmutableBrush FuelInk { get; init; }

    public required IImmutableBrush Footway { get; init; }

    public required IImmutableBrush Cycleway { get; init; }

    public required IImmutableBrush Track { get; init; }

    public required IImmutableBrush Rail { get; init; }

    /// <summary>The outline of the water point whose card is open.</summary>
    public required IImmutableBrush SelectedRing { get; init; }

    /// <summary>OSM landuse kind -> fill; a kind not listed is not drawn.</summary>
    public required IReadOnlyDictionary<string, IImmutableBrush> Landuse { get; init; }

    /// <summary>OSM highway value -> fill and casing, for every road class the renderer draws.</summary>
    public required IReadOnlyDictionary<string, RoadColours> Roads { get; init; }

    private static ImmutableSolidColorBrush Solid(uint rgb) =>
        new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}

/// <summary>A road's fill and the casing drawn around it.</summary>
public sealed record RoadColours(IImmutableBrush Fill, IImmutableBrush Casing);
