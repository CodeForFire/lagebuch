using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Content of the "Tastenkürzel" overlay (#544), opened by F1 or the command bar's ? button. Its
/// rows are read from <see cref="ShortcutRegistry"/>, the same table the key handler dispatches
/// on, so the overview cannot list a shortcut that does not exist or miss one that does.
/// </summary>
public sealed partial class ShortcutOverviewViewModel : ObservableObject
{
    public IReadOnlyList<ShortcutRow> Rows { get; } =
        ShortcutRegistry.All.Select(s => new ShortcutRow(s.Chord.Display, s.Description)).ToList();

    /// <summary>Raised after Close so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}

/// <summary>One line of the shortcut overview: the keys as a German keyboard labels them, and what they do.</summary>
public sealed record ShortcutRow(string Keys, string Description);
