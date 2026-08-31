using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Wasserförderung region of operation (#150 phase 2, region-pack follow-up):
/// a name and a folder path expected to hold <c>region.mbtiles</c> and <c>region.dem</c>.
/// Primarily populated by downloading a published pack from <see cref="IRegionPackCatalogService"/>
/// via <see cref="IRegionPackInstaller"/> — manual <see cref="Name"/>/<see cref="FolderPath"/> entry
/// stays available as a fallback for a self-built or hand-placed pack.
/// </summary>
public sealed partial class EinsatzgebietSection : EditorSection
{
    private readonly Action _onChanged;
    private readonly IRegionPackCatalogService _catalog;
    private readonly IRegionPackInstaller _installer;

    public EinsatzgebietSection(
        string title,
        Einsatzgebiet einsatzgebiet,
        Action onChanged,
        IRegionPackCatalogService catalog,
        IRegionPackInstaller installer)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(einsatzgebiet);
        _onChanged = onChanged;
        _catalog = catalog;
        _installer = installer;
        _name = einsatzgebiet.Name;
        _folderPath = einsatzgebiet.FolderPath;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KartendatenGefunden))]
    [NotifyPropertyChangedFor(nameof(KartendatenStatus))]
    private string _folderPath = string.Empty;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnFolderPathChanged(string value) => _onChanged();

    public Einsatzgebiet ToEinsatzgebiet() => new(Name, FolderPath);

    // --- Region-pack catalog / download ---
    public ObservableCollection<RegionPackInfo> AvailableRegions { get; } = new();

    [ObservableProperty]
    private RegionPackInfo? _selectedRegion;

    [ObservableProperty]
    private string? _catalogStatus;

    [ObservableProperty]
    private double _downloadProgress;

    [RelayCommand]
    private async Task LoadCatalog()
    {
        var regions = await _catalog.GetAvailableRegionsAsync();
        AvailableRegions.Clear();
        foreach (var region in regions)
        {
            AvailableRegions.Add(region);
        }

        CatalogStatus = AvailableRegions.Count == 0
            ? "Keine Regionen verfügbar — bitte Internetverbindung prüfen, oder Ordner manuell angeben."
            : null;
    }

    private bool CanDownloadSelectedRegion => SelectedRegion is not null;

    [RelayCommand(CanExecute = nameof(CanDownloadSelectedRegion))]
    private async Task DownloadSelectedRegion()
    {
        var region = SelectedRegion!;
        DownloadProgress = 0;
        var progress = new Progress<double>(p => DownloadProgress = p);
        var folder = await _installer.DownloadAndInstallAsync(region, progress);

        Name = region.Name;
        FolderPath = folder;
        _onChanged();
    }

    partial void OnSelectedRegionChanged(RegionPackInfo? value) => DownloadSelectedRegionCommand.NotifyCanExecuteChanged();

    // --- File-presence validation: is region.mbtiles/region.dem actually at FolderPath? ---
    public bool KartendatenGefunden =>
        !string.IsNullOrWhiteSpace(FolderPath)
        && File.Exists(Path.Combine(FolderPath, "region.mbtiles"))
        && File.Exists(Path.Combine(FolderPath, "region.dem"));

    public string? KartendatenStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                return null;
            }

            var mbtilesFound = File.Exists(Path.Combine(FolderPath, "region.mbtiles"));
            var demFound = File.Exists(Path.Combine(FolderPath, "region.dem"));
            if (mbtilesFound && demFound)
            {
                return "✓ Kartendaten gefunden.";
            }

            var missing = new List<string>();
            if (!mbtilesFound)
            {
                missing.Add("region.mbtiles");
            }

            if (!demFound)
            {
                missing.Add("region.dem");
            }

            return $"✗ Fehlt: {string.Join(", ", missing)}.";
        }
    }
}
