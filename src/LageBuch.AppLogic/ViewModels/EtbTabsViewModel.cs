using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The ETB rail entry: the Einsatztagebuch and the Lagemeldung (#592) as two tabs of one module.
/// A Lagemeldung is written from what the journal says and ends as a journal entry, so it lives
/// beside the journal rather than as a rail entry of its own.
/// </summary>
/// <remarks>
/// Only a frame. Both modules stay what they were and are still built, rebuilt and disposed by
/// <see cref="IncidentWorkspaceViewModel"/>; this holds no session subscription of its own.
/// </remarks>
public sealed partial class EtbTabsViewModel : ObservableObject, INarrowAware
{
    /// <summary>The EINSATZTAGEBUCH tab, which the ETB opens on.</summary>
    public const int JournalTab = 0;

    /// <summary>The LAGEMELDUNG tab.</summary>
    public const int SituationReportTab = 1;

    public EtbTabsViewModel(EtbViewModel journal, SituationReportsViewModel situationReports, int selectedTabIndex = JournalTab)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(situationReports);

        Journal = journal;
        SituationReports = situationReports;

        // Stryker disable once Equality : JournalTab is 0 and also the fallback, so 0 lands on it either way.
        _selectedTabIndex = selectedTabIndex is >= JournalTab and <= SituationReportTab ? selectedTabIndex : JournalTab;
    }

    public EtbViewModel Journal { get; }

    public SituationReportsViewModel SituationReports { get; }

    /// <summary>The open tab, <see cref="JournalTab"/> or <see cref="SituationReportTab"/>.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isNarrow;

    // Each tab lays out differently on a phone, and the workspace hands the flag only to the
    // modules in its rail -- this is the one it sees.
    partial void OnIsNarrowChanged(bool value)
    {
        Journal.IsNarrow = value;
        SituationReports.IsNarrow = value;
    }
}
