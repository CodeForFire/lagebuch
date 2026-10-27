using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Führung rail entry: the Führungsorganisation board, Funktionen, Kräfte and the Lagekarte as
/// tabs of one module. They describe the same structure — who leads, who holds which post, which unit
/// sits where — and as three rail entries they crowded the rail and the phone's bottom bar.
/// </summary>
/// <remarks>
/// Only a frame. The three modules stay what they were and are still built, rebuilt and disposed
/// by <see cref="IncidentWorkspaceViewModel"/>; this holds no session subscription of its own.
/// </remarks>
public sealed partial class LeadershipTabsViewModel : ObservableObject, INarrowAware
{
    /// <summary>The ORGANISATION tab, which Führung opens on.</summary>
    public const int OrganisationTab = 0;

    /// <summary>The FUNKTIONEN tab.</summary>
    public const int RolesTab = 1;

    /// <summary>The KRÄFTE tab.</summary>
    public const int ForcesTab = 2;

    /// <summary>The KARTE tab.</summary>
    public const int MapTab = 3;

    public LeadershipTabsViewModel(LeadershipViewModel board, RolesViewModel roles, ForcesViewModel forces, LagekarteViewModel map, int selectedTabIndex = OrganisationTab)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(forces);
        ArgumentNullException.ThrowIfNull(map);

        Board = board;
        Roles = roles;
        Forces = forces;
        Map = map;
        _selectedTabIndex = selectedTabIndex is >= OrganisationTab and <= MapTab ? selectedTabIndex : OrganisationTab;
    }

    public LeadershipViewModel Board { get; }

    public RolesViewModel Roles { get; }

    public ForcesViewModel Forces { get; }

    public LagekarteViewModel Map { get; }

    /// <summary>The open tab, one of <see cref="OrganisationTab"/>, <see cref="RolesTab"/>, <see cref="ForcesTab"/> and <see cref="MapTab"/>.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isNarrow;

    // Each tab lays out differently on a phone, and the workspace hands the flag only to the
    // modules in its rail -- this is the one it sees.
    partial void OnIsNarrowChanged(bool value)
    {
        Board.IsNarrow = value;
        Roles.IsNarrow = value;
        Forces.IsNarrow = value;
        Map.IsNarrow = value;
    }
}
