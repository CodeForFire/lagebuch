namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The Kartenpakete on this device and the catalogue they come from. A pack belongs to the device,
/// not to the Stammdaten: the Stammdaten travel to every joined client, and a folder on the host
/// means nothing on a tablet.
/// </summary>
public interface IRegionPackLibrary
{
    /// <summary>The packs the catalogue offers.</summary>
    /// <exception cref="RegionPackException">The catalogue could not be fetched or read.</exception>
    Task<IReadOnlyList<RegionPackInfo>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>The packs installed on this device, by name. A folder that is not a complete pack is left out.</summary>
    /// <exception cref="RegionPackException">The pack folder could not be read.</exception>
    IReadOnlyList<InstalledRegionPack> GetInstalled();

    /// <summary>
    /// Downloads <paramref name="pack"/> and installs it, replacing an older copy of the same pack.
    /// <paramref name="progress"/> runs from 0 to 1 while downloading and unpacking.
    /// </summary>
    /// <exception cref="RegionPackException">The download or installation failed; an older copy stays in place.</exception>
    Task<InstalledRegionPack> InstallAsync(RegionPackInfo pack, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes an installed pack from the device.</summary>
    /// <exception cref="RegionPackException">The pack's folder could not be deleted.</exception>
    void Remove(InstalledRegionPack pack);
}
