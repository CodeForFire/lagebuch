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
    [NotifyPropertyChangedFor(nameof(IncidentNumberFormatError))]
    private string? _incidentNumberFormat;

    /// <summary>
    /// Formats offered in the field, Bayern's first. Each is a published example, not a standard:
    /// every Leitstelle numbers its own way, so the field stays free text. In order: Bayern
    /// (Einsatzart, the spec's version 1.2, date, running number: "R 1.2 260715 0042");
    /// Baden-Württemberg, from the Landesfeuerwehrschule's F3 material ("B 240511 112"); and the
    /// Leitstellen-Schnittstelle UCRI's example ("2021-01-05-2553").
    /// </summary>
    public static IReadOnlyList<IncidentNumberFormatSuggestion> IncidentNumberFormatSuggestions { get; } =
    [
        new("{A} 1.2 {JJMMTT} {NNNN}", "Bayern"),
        new("{A} {JJMMTT} {NNN}", "Baden-Württemberg"),
        new("{JJJJ}-{MM}-{TT}-{NNNN}", "Leitstellen-Schnittstelle UCRI"),
    ];

    /// <summary>
    /// Why the dialog will pre-fill nothing from <see cref="IncidentNumberFormat"/>, or null when it
    /// is blank or readable. Saving is not refused: the template is kept so it can be corrected.
    /// </summary>
    public string? IncidentNumberFormatError =>
        string.IsNullOrWhiteSpace(IncidentNumberFormat) || Domain.ValueObjects.IncidentNumberFormat.TryParse(IncidentNumberFormat, out _)
            ? null
            : "Format nicht lesbar, es wird nichts vorausgefüllt. Platzhalter: {A}, {JJMMTT} oder {JJJJ}, {MM}, {TT}, {NNNN}.";

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
