using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the operational defaults (<see cref="IncidentSettings"/>): the Leitstelle and the
/// intervals of the Rückmeldung reminder, the Rückzugsdruck and the Einsatznummer's format (#462).
/// Unlike the list sections these are scalars, so the section exposes one bindable property each and reports any change through the
/// shared dirty callback. The Einsatzzeiten used to sit here too, one per hard-coded Trupp-Typ name; they are
/// edited on the Trupp-Typen themselves now (#398).
/// </summary>
public sealed partial class SettingsSection : EditorSection
{
    private readonly Action _onChanged;

    public SettingsSection(string title, IncidentSettings settings, Action onChanged)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _onChanged = onChanged;
        _ilsReminderIntervalMinutes = settings.IlsReminderIntervalMinutes;
        _ilsReminderFollowUpIntervalMinutes = settings.IlsReminderFollowUpIntervalMinutes;
        _returnPressureBar = settings.ReturnPressureBar;
        _dispatchCentreName = settings.DispatchCentreName;
        _incidentNumberFormat = settings.IncidentNumberFormat;
    }

    [ObservableProperty]
    private int _ilsReminderIntervalMinutes;

    [ObservableProperty]
    private int _ilsReminderFollowUpIntervalMinutes;

    [ObservableProperty]
    private int _returnPressureBar;

    /// <summary>The Leitstelle the Rückmeldung goes to, "ILS" unless configured (#400).</summary>
    [ObservableProperty]
    private string _dispatchCentreName;

    /// <summary>The Einsatznummer template the EINSATZDATEN dialog pre-fills from, if any (#462).</summary>
    [ObservableProperty]
    private string? _incidentNumberFormat;

    partial void OnIlsReminderIntervalMinutesChanged(int value) => _onChanged();

    partial void OnIlsReminderFollowUpIntervalMinutesChanged(int value) => _onChanged();

    partial void OnReturnPressureBarChanged(int value) => _onChanged();

    partial void OnDispatchCentreNameChanged(string value) => _onChanged();

    partial void OnIncidentNumberFormatChanged(string? value) => _onChanged();

    public IncidentSettings ToSettings() => new(
        IlsReminderIntervalMinutes,
        IlsReminderFollowUpIntervalMinutes,
        ReturnPressureBar,
        DispatchCentreName,
        IncidentNumberFormat);
}
