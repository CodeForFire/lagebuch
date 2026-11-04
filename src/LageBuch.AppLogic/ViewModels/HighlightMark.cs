using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Whether one line on the Führung board is lit because the pointer rests on the same value
/// elsewhere. Which lines light is <see cref="LeadershipViewModel.HighlightFunkgruppe"/>'s decision.
/// </summary>
public sealed partial class HighlightMark : ObservableObject
{
    [ObservableProperty]
    private bool _isHighlighted;
}
