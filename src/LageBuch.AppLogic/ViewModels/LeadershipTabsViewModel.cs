using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Führung rail entry: the Führungsorganisation board, Funktionen and Kräfte as three tabs of
/// one module. They describe the same structure — who leads, who holds which post, which unit
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

    public LeadershipTabsViewModel(LeadershipViewModel board, RolesViewModel roles, ForcesViewModel forces, int selectedTabIndex = OrganisationTab)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(forces);

        Board = board;
        Roles = roles;
        Forces = forces;
        _selectedTabIndex = selectedTabIndex is >= OrganisationTab and <= ForcesTab ? selectedTabIndex : OrganisationTab;
    }

    public LeadershipViewModel Board { get; }

    public RolesViewModel Roles { get; }

    public ForcesViewModel Forces { get; }

    /// <summary>The open tab, one of <see cref="OrganisationTab"/>, <see cref="RolesTab"/> and <see cref="ForcesTab"/>.</summary>
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
    }
}
