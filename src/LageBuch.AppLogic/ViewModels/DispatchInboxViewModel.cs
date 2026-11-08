using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Alarm import on the Home screen: the folder alarm mails land in, and the newest alarm there
/// still to be answered. EINSATZ ANLEGEN hands the alarm to the shell, which asks for the
/// Lagebuchführer as for any new incident and then takes it up with <see cref="CurrentImport"/>;
/// VERWERFEN answers it without an incident. Either way it is never offered again.
/// </summary>
public sealed partial class DispatchInboxViewModel : ObservableObject, IDisposable
{
    /// <summary>Ticks between two looks into the folder; the shared ticker fires once a second.</summary>
    public const int TicksPerScan = 10;

    private readonly IDispatchInboxStore _store;
    private readonly IMasterDataProvider _masterData;
    private readonly IFileDialogService _dialogs;
    private readonly IClock _clock;
    private readonly DispatchFolderScanner _scanner;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Action<Action> _runInBackground;
    private readonly IDisposable _tick;
    private int _ticks;
    private bool _scanning;

    public DispatchInboxViewModel(
        IDispatchInboxStore store,
        IMasterDataProvider masterData,
        IFileDialogService dialogs,
        IClock clock,
        ITicker ticker,
        IUiDispatcher? uiDispatcher = null,
        Action<Action>? runInBackground = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(ticker);
        _store = store;
        _masterData = masterData;
        _dialogs = dialogs;
        _clock = clock;
        _scanner = new DispatchFolderScanner(clock, zone);
        _uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        _runInBackground = runInBackground ?? (work => work());
        _folder = store.GetFolder();
        _tick = ticker.Subscribe(OnTick);
        Scan();
    }

    /// <summary>Raised by EINSATZ ANLEGEN; the shell asks for the Lagebuchführer, then reads <see cref="CurrentImport"/>.</summary>
    public Action? CreateRequested { get; set; }

    /// <summary>The alarm folder, or null while alarm import is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    private string? _folder;

    public bool HasFolder => Folder is not null;

    /// <summary>The alarm to offer, or null.</summary>
    [ObservableProperty]
    private DispatchOfferViewModel? _offer;

    /// <summary>Why the folder could not be read or set, or null.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Looks into the folder now, off the UI thread; a look already running is not doubled.</summary>
    public void Scan()
    {
        if (Folder is not { } folder || _scanning)
        {
            return;
        }

        _scanning = true;
        IReadOnlySet<string> handled;
        try
        {
            handled = _store.GetHandled();
        }
        catch (IOException ex)
        {
            _scanning = false;
            Error = $"Alarm-Ordner: {ex.Message}";
            return;
        }

        _runInBackground(() =>
        {
            var scan = ScanSafely(folder, handled);
            _uiDispatcher.Post(() => Apply(folder, scan));
        });
    }

    // Runs on a pool thread, where an escaping exception would vanish and leave _scanning set for
    // good, so no further alarm would ever be looked for. Whatever goes wrong becomes the banner's
    // error line instead, and the next tick tries again.
    [SuppressMessage("Design", "CA1031", Justification = "Surfaces as the bound Error string; the next poll retries.")]
    private DispatchScan ScanSafely(string folder, IReadOnlySet<string> handled)
    {
        try
        {
            return _scanner.Scan(folder, handled);
        }
        catch (Exception ex)
        {
            return new DispatchScan(null, $"Alarm-Ordner nicht lesbar: {ex.Message}");
        }
    }

    /// <summary>The offered alarm with the Einsatzmittel as ticked right now, or null when there is none.</summary>
    public DispatchImport? CurrentImport() => Offer?.ToImport();

    /// <summary>
    /// Records <paramref name="alarm"/> as taken up once its incident exists. Not before: a
    /// cancelled save picker leaves the alarm on offer, to be taken up on the next try.
    /// </summary>
    public void MarkTaken(DispatchAlarm alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        if (string.Equals(Offer?.Alarm.Key, alarm.Key, StringComparison.Ordinal))
        {
            Offer = null;
        }

        Remember(alarm);
    }

    public void Dispose() => _tick.Dispose();

    private void OnTick()
    {
        if (++_ticks % TicksPerScan == 0)
        {
            Scan();
        }
    }

    private void Apply(string folder, DispatchScan scan)
    {
        _scanning = false;
        if (!string.Equals(folder, Folder, StringComparison.Ordinal))
        {
            return; // the folder changed while this look was running; its own look follows
        }

        Error = scan.Error;

        // The same alarm again keeps the offer on screen as it is, ticks and all.
        if (scan.Newest?.Key != Offer?.Alarm.Key)
        {
            Offer = scan.Newest is { } alarm ? new DispatchOfferViewModel(alarm, _masterData.Get().Vehicles) : null;
        }
    }

    [RelayCommand]
    private void Accept() => CreateRequested?.Invoke();

    [RelayCommand]
    private void Dismiss()
    {
        if (Offer is { } offer)
        {
            Answer(offer);
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        if (await _dialogs.PickFolderAsync(Folder) is { } folder)
        {
            SetFolder(folder);
        }
    }

    [RelayCommand]
    private void ClearFolder() => SetFolder(null);

    private void SetFolder(string? folder)
    {
        try
        {
            _store.SetFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Alarm-Ordner nicht gespeichert: {ex.Message}";
            return;
        }

        Folder = folder;
        Offer = null;
        Error = null;
        Scan();
    }

    private void Answer(DispatchOfferViewModel offer)
    {
        Offer = null;
        Remember(offer.Alarm);
    }

    private void Remember(DispatchAlarm alarm)
    {
        try
        {
            _store.MarkHandled(alarm.Key, _clock.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Alarm nicht als erledigt gespeichert: {ex.Message}";
        }
    }
}
