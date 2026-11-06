using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Kartenpakete: one list of every pack this device holds or the catalogue offers, each row
/// with its own state and action, and a look at one of them. Unlike every other Stammdaten section
/// it holds nothing SPEICHERN writes -- a pack belongs to the device and acts at once -- so it never
/// marks the editor dirty, and the editor keeps the one instance across a reload so that a running
/// download is not lost.
/// </summary>
public sealed partial class MapPacksSection : EditorSection
{
    private readonly IRegionPackLibrary _library;

    // Null until the catalogue was fetched once; the installed packs are listed without it.
    private IReadOnlyList<RegionPackInfo>? _catalogue;

    // The running download, so ABBRECHEN can stop it.
    private CancellationTokenSource? _installCancel;

    // What the preview has open, so refilling the list does not reopen and recentre the same map.
    private InstalledRegionPack? _shown;

    // Set while Packs is refilled: the selection passes through null then, which is no choice.
    private bool _rebuilding;

    public MapPacksSection(string title, IRegionPackLibrary library, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        Rebuild();
    }

    /// <summary>Every pack by name: the installed ones and, once fetched, what the catalogue offers.</summary>
    public ObservableCollection<MapPackRow> Packs { get; } = new();

    /// <summary>The map below the list, showing <see cref="SelectedPack"/>.</summary>
    public MapViewport Preview { get; } = new();

    public bool HasInstalled => Packs.Any(p => p.Installed is not null);

    /// <summary>The selected pack is not on this device, so the map has nothing to show for it.</summary>
    public bool HasNoPreview => SelectedPack is { Installed: null };

    /// <summary>
    /// The rail's second line under "Karten": what is on the device and how many updates the
    /// catalogue offers, or how far the running download is.
    /// </summary>
    public string RailSummary
    {
        get
        {
            if (Packs.FirstOrDefault(p => p.IsDownloading) is { } loading)
            {
                return $"Lädt … {loading.Percent} %";
            }

            var installed = Packs.Count(p => p.Installed is not null);
            if (installed == 0)
            {
                return "Nichts installiert";
            }

            var summary = $"{Formatting.Number(installed)} installiert";
            return Packs.Count(p => p.State == MapPackState.UpdateAvailable) switch
            {
                0 => summary,
                1 => $"{summary} · 1 Update",
                var updates => $"{summary} · {Formatting.Number(updates)} Updates",
            };
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPreview))]
    private MapPackRow? _selectedPack;

    /// <summary>What went wrong, or what just finished; German, shown under the list.</summary>
    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCatalogCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _isBusy;

    /// <summary>The catalogue is being fetched.</summary>
    [ObservableProperty]
    private bool _isCatalogLoading;

    partial void OnSelectedPackChanged(MapPackRow? value)
    {
        if (!_rebuilding)
        {
            ShowPack(value?.Installed);
        }
    }

    /// <summary>
    /// Fetches the catalogue the first time the section is opened, so the list is complete without
    /// a button press. A failure surfaces in <see cref="Status"/>; opening the section again retries.
    /// </summary>
    public void EnsureCatalogLoaded()
    {
        if (_catalogue is null && LoadCatalogCommand.CanExecute(null))
        {
            // LoadCatalog turns every RegionPackException into Status.
            _ = LoadCatalogCommand.ExecuteAsync(null);
        }
    }

    private bool CanLoadCatalog => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanLoadCatalog))]
    private async Task LoadCatalog()
    {
        IsBusy = true;
        IsCatalogLoading = true;
        Status = null;
        try
        {
            _catalogue = await _library.GetCatalogAsync();
            Rebuild();
            if (_catalogue.Count == 0)
            {
                Status = "Die Liste enthält keine Kartenpakete.";
            }
        }
        catch (RegionPackException e)
        {
            Status = HasInstalled ? $"{e.Message} Installierte Karten funktionieren weiter." : e.Message;
        }
        finally
        {
            IsCatalogLoading = false;
            IsBusy = false;
        }
    }

    private bool CanInstall(MapPackRow? row) => !IsBusy && row is { ActionLabel: not null };

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install(MapPackRow? row)
    {
        if (row?.Offered is not { } offered)
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _installCancel = cancel;
        IsBusy = true;
        Status = null;
        row.Progress = 0;
        row.IsDownloading = true;
        OnPropertyChanged(nameof(RailSummary));
        var keep = SelectedPack?.Slug;
        string? select = null;
        try
        {
            // The open map would hold the file the install is about to replace.
            if (string.Equals(keep, row.Slug, StringComparison.Ordinal))
            {
                SelectedPack = null;
            }

            var progress = new RowProgress(p =>
            {
                row.Progress = p;
                OnPropertyChanged(nameof(RailSummary));
            });
            var installed = await _library.InstallAsync(offered, progress, cancel.Token);
            Status = $"„{offered.Name}“ ist installiert.";
            select = installed.Info.Slug;
        }
        catch (OperationCanceledException)
        {
            Status = "Download abgebrochen.";
        }
        catch (RegionPackException e)
        {
            Status = e.Message;
        }
        finally
        {
            _installCancel = null;
            row.IsDownloading = false;
            IsBusy = false;
            Rebuild(select ?? keep);
        }
    }

    /// <summary>Stops the running download; the device keeps what it had before.</summary>
    [RelayCommand]
    private void CancelInstall() => _installCancel?.Cancel();

    private bool CanRemove(MapPackRow? row) => !IsBusy && row is { Installed: not null };

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove(MapPackRow? row)
    {
        if (row?.Installed is not { } pack)
        {
            return;
        }

        // Stryker disable once Boolean : a pack row is never one the section added blank, so isBlank changes nothing.
        RemoveAfterConfirm(row, row.Name, isBlank: false, () =>
        {
            if (string.Equals(SelectedPack?.Slug, row.Slug, StringComparison.Ordinal))
            {
                SelectedPack = null;
            }

            try
            {
                _library.Remove(pack);
                Status = null;
            }
            catch (RegionPackException e)
            {
                Status = e.Message;
            }

            Rebuild();
        });
    }

    private void ShowPack(InstalledRegionPack? pack)
    {
        if (pack == _shown)
        {
            return;
        }

        _shown = pack;
        Preview.Show(pack);
    }

    // Lists the installed packs and the catalogue as one, by name, and selects the installed pack
    // named by select, else the one selected before, else the first installed one.
    private void Rebuild(string? select = null)
    {
        select ??= SelectedPack?.Slug;
        IReadOnlyList<InstalledRegionPack> installed = [];
        try
        {
            installed = _library.GetInstalled();
        }
        catch (RegionPackException e)
        {
            Status = e.Message;
        }

        var offered = (_catalogue ?? []).ToDictionary(p => p.Slug, StringComparer.Ordinal);
        var installedSlugs = installed.Select(i => i.Info.Slug).ToHashSet(StringComparer.Ordinal);
        var rows = installed
            .Select(i => new MapPackRow(offered.GetValueOrDefault(i.Info.Slug), i))
            .Concat(offered.Values.Where(o => !installedSlugs.Contains(o.Slug)).Select(o => new MapPackRow(o, null)))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _rebuilding = true;
        try
        {
            Packs.Clear();
            foreach (var row in rows)
            {
                Packs.Add(row);
            }

            SelectedPack = Packs.FirstOrDefault(p => p.Installed is not null && string.Equals(p.Slug, select, StringComparison.Ordinal))
                ?? Packs.FirstOrDefault(p => p.Installed is not null);
        }
        finally
        {
            _rebuilding = false;
        }

        ShowPack(SelectedPack?.Installed);
        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(RailSummary));
        InstallCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    // "Stand 27.10.2026 · Version 1 · 253 MB". A build time that is no ISO timestamp is shown as it came.
    internal static string Detail(RegionPackInfo info) =>
        $"Stand {BuiltDay(info)} · Version {Formatting.Number(info.Version)} · {Size(info.SizeBytes)}";

    internal static string BuiltDay(RegionPackInfo info) =>
        DateTimeOffset.TryParse(info.BuiltAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? Formatting.Date(t)
            : info.BuiltAt;

    internal static string Size(long bytes) => $"{Formatting.Number((int)Math.Max(1, Math.Ceiling(bytes / (1024.0 * 1024.0))))} MB";

    // Reports where it is called. The library awaits without ConfigureAwait(false), so it reports
    // on the UI context already; Progress<T> would only post each report once more, after the
    // install may have finished and reset the row.
    private sealed class RowProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

/// <summary>Where a Kartenpaket stands on this device.</summary>
public enum MapPackState
{
    /// <summary>The catalogue offers it; the device does not hold it.</summary>
    NotInstalled,

    /// <summary>The device holds it, and the catalogue offers another edition.</summary>
    UpdateAvailable,

    /// <summary>The device holds it, and there is nothing newer to fetch, or no catalogue to ask.</summary>
    Installed,
}

/// <summary>One Kartenpaket: on this device, in the catalogue, or both.</summary>
public sealed partial class MapPackRow : ObservableObject
{
    private readonly RegionPackInfo _shown;

    /// <param name="offered">The catalogue's edition, or null when the catalogue is not loaded or lacks the pack.</param>
    /// <param name="installed">The device's copy, or null when it has none.</param>
    public MapPackRow(RegionPackInfo? offered, InstalledRegionPack? installed)
    {
        Offered = offered;
        Installed = installed;
        _shown = installed?.Info ?? offered ?? throw new ArgumentException("A pack row needs an installed or an offered pack.", nameof(offered));
    }

    public RegionPackInfo? Offered { get; }

    public InstalledRegionPack? Installed { get; }

    public string Slug => _shown.Slug;

    public string Name => _shown.Name;

    /// <summary>The installed edition when there is one, else the one on offer.</summary>
    public string Detail => MapPacksSection.Detail(_shown);

    public MapPackState State =>
        Installed is null ? MapPackState.NotInstalled
        : Offered is null || IsSameEdition(Installed.Info, Offered) ? MapPackState.Installed
        : MapPackState.UpdateAvailable;

    /// <summary>The device holds this pack and nothing newer is on offer.</summary>
    public bool IsCurrent => State == MapPackState.Installed;

    public bool HasUpdate => State == MapPackState.UpdateAvailable;

    /// <summary>The download button's label, or null when there is nothing to fetch.</summary>
    public string? ActionLabel => State switch
    {
        MapPackState.NotInstalled => "HERUNTERLADEN",
        MapPackState.UpdateAvailable => "AKTUALISIEREN",
        _ => null,
    };

    public bool ShowsAction => ActionLabel is not null && !IsDownloading;

    public bool ShowsRemove => Installed is not null && !IsDownloading;

    public bool ShowsDetail => !IsDownloading;

    /// <summary>Why AKTUALISIEREN is offered, so an update with the same version number is not a riddle; null otherwise.</summary>
    public string? UpdateReason => State != MapPackState.UpdateAvailable || Installed?.Info is not { } installed || Offered is not { } offered ? null
        : installed.Version != offered.Version ? $"Neue Version {Formatting.Number(offered.Version)} – installiert ist Version {Formatting.Number(installed.Version)}."
        : $"Neu gebaut am {MapPacksSection.BuiltDay(offered)} – installiert ist der Stand vom {MapPacksSection.BuiltDay(installed)}.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAction))]
    [NotifyPropertyChangedFor(nameof(ShowsRemove))]
    [NotifyPropertyChangedFor(nameof(ShowsDetail))]
    private bool _isDownloading;

    /// <summary>0..1 while this pack downloads.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _progress;

    public int Percent => (int)Math.Floor(Math.Clamp(Progress, 0, 1) * 100);

    /// <summary>"41 % von 109 MB" while the pack downloads.</summary>
    public string ProgressText => $"{Percent} % von {MapPacksSection.Size((Offered ?? _shown).SizeBytes)}";

    // Exactly this edition: the same version and the same file.
    private static bool IsSameEdition(RegionPackInfo installed, RegionPackInfo offered) =>
        installed.Version == offered.Version
        && string.Equals(installed.Sha256, offered.Sha256, StringComparison.Ordinal);
}
