using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The dialog behind the Leitstelle badge on the Führung board (#926): the one channel the ELW reaches
/// the Leitstelle on, e.g. "TMO WR_FFB" while a Wasserrettung leads. Buffered until SPEICHERN;
/// <see cref="Closed"/> fires on every outcome so the host clears the overlay.
/// </summary>
/// <remarks>
/// One field, so ABBRECHEN closes at once instead of asking whether the typed text may go, as the
/// larger forms do (#652): retyping a channel costs less than the question.
/// </remarks>
public sealed partial class DispatchChannelDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action? _onChanged;

    /// <param name="session">The Einsatz whose channel is edited; its current one seeds the field.</param>
    /// <param name="masterData">Offers its TMO and DMO Funkgruppen as suggestions.</param>
    /// <param name="onChanged">Called after a save that went through, so the host can persist.</param>
    public DispatchChannelDialogViewModel(IIncidentSession session, MasterDataSet masterData, Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _onChanged = onChanged;
        Title = $"Kanal zur {masterData.Settings.DispatchCentreName}";

        // Favourites first within each kind (#833); the field holds the kind too, since one channel
        // can be either: "TMO WR_FFB".
        ChannelOptions =
        [
            .. Texts(masterData.TalkGroups, TalkGroupKind.Tmo, "TMO"),
            .. Texts(masterData.TalkGroups, TalkGroupKind.Dmo, "DMO"),
        ];
        _channel = session.Incident.DispatchChannel;
    }

    public event EventHandler? Closed;

    /// <summary>"Kanal zur ILS", after the Leitstelle the Stammdaten name.</summary>
    public string Title { get; }

    /// <summary>The Stammdaten's Funkgruppen with their kind. Suggestions only; any other channel is typed in.</summary>
    public IReadOnlyList<string> ChannelOptions { get; }

    /// <summary>The channel as typed; blank clears it on save.</summary>
    [ObservableProperty]
    private string? _channel;

    /// <summary>Why the last save was refused, in German; null otherwise.</summary>
    [ObservableProperty]
    private string? _saveError;

    private bool CanSave => !_session.IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveError = null;
        var typed = StammdatenCatalogue.Normalize(Channel, ChannelOptions);
        var stored = string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
        if (!string.Equals(stored, _session.Incident.DispatchChannel, StringComparison.Ordinal))
        {
            try
            {
                _session.SetDispatchChannel(stored);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                SaveError = DomainRefusal.Message(ex);
                return;
            }

            _onChanged?.Invoke();
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ABBRECHEN, and Esc: closes without saving.</summary>
    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    private static IEnumerable<string> Texts(IEnumerable<TalkGroup> groups, TalkGroupKind kind, string prefix) =>
        TalkGroup.FavouritesFirst(groups.Where(g => g.Kind == kind), g => g.Label).Select(g => $"{prefix} {g.Label}");
}
