using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Kartenpakete on this device: which are installed, which the catalogue offers, and a look at
/// one of them. Unlike every other Stammdaten section it holds nothing SPEICHERN writes -- a pack
/// belongs to the device and acts at once -- so it never marks the editor dirty, and the editor
/// keeps the one instance across a reload so that a running download is not lost.
/// </summary>
public sealed partial class MapPacksSection : EditorSection
{
    private readonly IRegionPackLibrary _library;

    public MapPacksSection(string title, IRegionPackLibrary library, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        Refresh();
    }

    public ObservableCollection<InstalledMapPackRow> Installed { get; } = new();

    /// <summary>The catalogue, empty until <see cref="LoadCatalogCommand"/> fetched it.</summary>
    public ObservableCollection<AvailableMapPackRow> Available { get; } = new();

    /// <summary>The map below the list, showing <see cref="SelectedPack"/>.</summary>
    public MapViewport Preview { get; } = new();

    public bool HasInstalled => Installed.Count > 0;

    [ObservableProperty]
    private InstalledMapPackRow? _selectedPack;

    /// <summary>What went wrong, or what just finished; German, shown under the buttons.</summary>
    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCatalogCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isBusy;

    /// <summary>0..1 while a pack downloads.</summary>
    [ObservableProperty]
    private double _progress;

    partial void OnSelectedPackChanged(InstalledMapPackRow? value) => Preview.Show(value?.Pack);

    private bool CanLoadCatalog => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanLoadCatalog))]
    private async Task LoadCatalog()
    {
        IsBusy = true;
        Status = null;
        try
        {
            var catalogue = await _library.GetCatalogAsync();
            Available.Clear();
            foreach (var info in catalogue.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Available.Add(new AvailableMapPackRow(info, Installed.FirstOrDefault(i => i.Pack.Info.Slug == info.Slug)?.Pack.Info));
            }

            if (Available.Count == 0)
            {
                Status = "Die Liste enthält keine Kartenpakete.";
            }
        }
        catch (RegionPackException e)
        {
            Status = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInstall(AvailableMapPackRow? row) => !IsBusy && row is { IsCurrent: false };

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install(AvailableMapPackRow? row)
    {
        if (row is null)
        {
            return;
        }

        IsBusy = true;
        Status = null;
        Progress = 0;
        try
        {
            // The open map would hold the file the install is about to replace.
            if (SelectedPack?.Pack.Info.Slug == row.Info.Slug)
            {
                SelectedPack = null;
            }

            var installed = await _library.InstallAsync(row.Info, new Progress<double>(p => Progress = p));
            Status = $"„{row.Info.Name}“ ist installiert.";
            Refresh();
            SelectedPack = Installed.FirstOrDefault(i => i.Pack.Info.Slug == installed.Info.Slug);
            MarkInstalled(installed.Info);
        }
        catch (RegionPackException e)
        {
            Status = e.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void Remove(InstalledMapPackRow? row)
    {
        if (row is null)
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, isBlank: false, () =>
        {
            if (ReferenceEquals(SelectedPack, row))
            {
                SelectedPack = null;
            }

            try
            {
                _library.Remove(row.Pack);
                Status = null;
            }
            catch (RegionPackException e)
            {
                Status = e.Message;
            }

            Refresh();
            MarkInstalled(null, row.Pack.Info.Slug);
        });
    }

    private void Refresh()
    {
        var selected = SelectedPack?.Pack.Info.Slug;
        Installed.Clear();
        try
        {
            foreach (var pack in _library.GetInstalled())
            {
                Installed.Add(new InstalledMapPackRow(pack));
            }
        }
        catch (RegionPackException e)
        {
            Status = e.Message;
        }

        OnPropertyChanged(nameof(HasInstalled));
        SelectedPack = Installed.FirstOrDefault(i => i.Pack.Info.Slug == selected) ?? Installed.FirstOrDefault();
    }

    // Swaps the catalogue row so that its button reflects what is now on the device.
    private void MarkInstalled(RegionPackInfo? installed, string? removedSlug = null)
    {
        var slug = installed?.Slug ?? removedSlug;
        for (var i = 0; i < Available.Count; i++)
        {
            if (Available[i].Info.Slug == slug)
            {
                Available[i] = new AvailableMapPackRow(Available[i].Info, installed);
            }
        }

        InstallCommand.NotifyCanExecuteChanged();
    }

    internal static string Size(long bytes) => $"{Formatting.Number((int)Math.Max(1, Math.Ceiling(bytes / (1024.0 * 1024.0))))} MB";
}

/// <summary>A Kartenpaket on this device.</summary>
public sealed class InstalledMapPackRow(InstalledRegionPack pack)
{
    public InstalledRegionPack Pack { get; } = pack;

    public string Name => Pack.Info.Name;

    public string Detail => $"Stand {Pack.Info.BuiltAt} · {MapPacksSection.Size(Pack.Info.SizeBytes)}";

    public string Attribution => Pack.Info.Attribution;
}

/// <summary>A Kartenpaket the catalogue offers, and whether this device already has that edition.</summary>
public sealed class AvailableMapPackRow(RegionPackInfo info, RegionPackInfo? installed)
{
    public RegionPackInfo Info { get; } = info;

    public string Name => Info.Name;

    public string Detail => $"Stand {Info.BuiltAt} · {MapPacksSection.Size(Info.SizeBytes)}";

    /// <summary>The device holds exactly this edition; there is nothing to download.</summary>
    public bool IsCurrent => installed is not null && installed.BuiltAt == Info.BuiltAt && installed.DownloadUrl == Info.DownloadUrl;

    public string ActionLabel => installed is null ? "HERUNTERLADEN" : IsCurrent ? "INSTALLIERT" : "AKTUALISIEREN";
}
