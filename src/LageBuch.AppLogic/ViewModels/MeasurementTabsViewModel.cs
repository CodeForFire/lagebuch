using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The MESSUNG rail entry (#930): the Gasmessprotokoll of a building and the ABC/Gefahrstoff
/// Messprotokoll as two tabs of one module. Both are measurements; the first is bound to a Haus,
/// Stockwerk and Wohnung, the second to free Messpunkte.
/// </summary>
/// <remarks>
/// Only a frame, like <see cref="EtbTabsViewModel"/>. Both modules are built, rebuilt and disposed
/// by <see cref="IncidentWorkspaceViewModel"/>; this holds no session subscription of its own.
/// </remarks>
public sealed partial class MeasurementTabsViewModel : ObservableObject, INarrowAware
{
    /// <summary>The GEBÄUDE tab, the Gasmessprotokoll, which the module opens on.</summary>
    public const int GebaeudeTab = 0;

    /// <summary>The MESSPROTOKOLL tab.</summary>
    public const int MessprotokollTab = 1;

    public MeasurementTabsViewModel(CoMessprotokollViewModel building, MeasurementLogViewModel log, int selectedTabIndex = GebaeudeTab)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(log);

        Building = building;
        Log = log;

        // Stryker disable once Equality : GebaeudeTab is 0 and also the fallback, so 0 lands on it either way.
        _selectedTabIndex = selectedTabIndex is >= GebaeudeTab and <= MessprotokollTab ? selectedTabIndex : GebaeudeTab;
    }

    /// <summary>The GEBÄUDE tab's content.</summary>
    public CoMessprotokollViewModel Building { get; }

    /// <summary>The MESSPROTOKOLL tab's content.</summary>
    public MeasurementLogViewModel Log { get; }

    /// <summary>The open tab, <see cref="GebaeudeTab"/> or <see cref="MessprotokollTab"/>.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isNarrow;

    // The workspace hands the flag only to the modules in its rail -- this is the one it sees.
    partial void OnIsNarrowChanged(bool value)
    {
        Building.IsNarrow = value;
        Log.IsNarrow = value;
    }
}
