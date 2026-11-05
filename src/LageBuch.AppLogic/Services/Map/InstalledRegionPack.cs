namespace LageBuch.AppLogic.Services.Map;

/// <summary>A Kartenpaket unpacked on this device.</summary>
/// <param name="Info">What the catalogue said about the pack when it was installed.</param>
/// <param name="FolderPath">The folder holding the pack's files, each under its FORMAT.md name.</param>
public sealed record InstalledRegionPack(RegionPackInfo Info, string FolderPath)
{
    /// <summary>The map, as vector tiles.</summary>
    public const string VectorTilesFileName = "map.pmtiles";

    /// <summary>The DGM5 heights, a GeoTIFF on the EPSG:25832 grid.</summary>
    public const string ElevationFileName = "elevation.tif";

    public const string WaterFileName = "water.geojson";

    public const string AddressesFileName = "addresses.sqlite";

    /// <summary>The pack's area, as its zip carries it.</summary>
    public const string BoundaryFileName = "boundary.geojson";

    public string VectorTilesPath => Path.Join(FolderPath, VectorTilesFileName);

    public string ElevationPath => Path.Join(FolderPath, ElevationFileName);

    public string WaterPath => Path.Join(FolderPath, WaterFileName);

    public string AddressesPath => Path.Join(FolderPath, AddressesFileName);

    public string BoundaryPath => Path.Join(FolderPath, BoundaryFileName);
}
