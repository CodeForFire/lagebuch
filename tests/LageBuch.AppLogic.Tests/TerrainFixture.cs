using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;
using LageBuch.Persistence.Tests;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// Made-up terrain around the Fürstenfeldbruck Marktplatz: a plane rising 1 m every 20 m to the
/// east from 550 m at the Marktplatz, on a 20 m grid 6 km square. Not DGM data.
/// </summary>
internal static class TerrainFixture
{
    public static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);

    public static readonly (double East, double North) MarktplatzUtm = Utm32.FromGeo(Marktplatz);

    public static readonly GeoTiffFixtures.Grid Grid = new(
        (Math.Floor(MarktplatzUtm.East / 20) * 20) - 3000,
        (Math.Floor(MarktplatzUtm.North / 20) * 20) + 3000,
        20,
        300,
        300);

    public static float Height(double east, double north) => (float)(550 + ((east - MarktplatzUtm.East) / 20));

    /// <summary>A pack folder under <paramref name="root"/> holding only the elevation file.</summary>
    public static InstalledRegionPack Pack(string root, string slug = "ffb", string name = "Landkreis Fürstenfeldbruck")
    {
        var folder = Directory.CreateDirectory(Path.Join(root, slug)).FullName;
        GeoTiffFixtures.Write(Path.Join(folder, InstalledRegionPack.ElevationFileName), Grid, Height, tileSide: 64);
        return new InstalledRegionPack(FakeRegionPackLibrary.Pack(slug, name), folder);
    }

    public static ElevationSet Elevation(string root) => new([new GeoTiffElevation(Pack(root).ElevationPath)]);
}
