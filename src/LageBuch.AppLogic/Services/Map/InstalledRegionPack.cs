namespace LageBuch.AppLogic.Services.Map;

/// <summary>A Kartenpaket unpacked on this device.</summary>
/// <param name="Info">What the catalogue said about the pack when it was installed.</param>
/// <param name="FolderPath">The folder holding the pack's files, each under its FORMAT.md name.</param>
public sealed record InstalledRegionPack(RegionPackInfo Info, string FolderPath)
{
    public const string TilesFileName = "map.mbtiles";

    public const string WaterFileName = "water.geojson";

    public const string AddressesFileName = "addresses.sqlite";

    public string TilesPath => Path.Join(FolderPath, TilesFileName);

    public string WaterPath => Path.Join(FolderPath, WaterFileName);

    public string AddressesPath => Path.Join(FolderPath, AddressesFileName);
}
