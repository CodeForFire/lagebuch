namespace LageBuch.AppLogic.Services;

/// <summary>Downloads and unpacks a region pack (#150 follow-up) into a local folder.</summary>
public interface IRegionPackInstaller
{
    /// <summary>Returns the folder the pack was extracted into (ready to use as Einsatzgebiet.FolderPath).</summary>
    Task<string> DownloadAndInstallAsync(RegionPackInfo pack, IProgress<double>? progress, CancellationToken ct = default);
}
