namespace LageBuch.AppLogic.Services.Map;

/// <summary>A Kartenpaket unpacked on this device.</summary>
/// <param name="Info">What the catalogue said about the pack when it was installed.</param>
/// <param name="FolderPath">The folder holding <c>region.mbtiles</c>.</param>
public sealed record InstalledRegionPack(RegionPackInfo Info, string FolderPath)
{
    public const string TilesFileName = "region.mbtiles";

    public string TilesPath => Path.Join(FolderPath, TilesFileName);
}
