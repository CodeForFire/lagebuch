using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Content of the "Tastenkürzel" overlay (#544), opened by F1 or the command bar's ? button. Its
/// rows are read from <see cref="ShortcutRegistry"/>, the same table the key handler dispatches
/// on, so the overview cannot list a shortcut that does not exist or miss one that does. The ten
/// rail digits share one row: listed singly they would only say "1. Eintrag" … "10. Eintrag".
/// </summary>
public sealed partial class ShortcutOverviewViewModel : ObservableObject
{
    public IReadOnlyList<ShortcutRow> Rows { get; } = BuildRows();

    /// <summary>Raised after Close so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    private static List<ShortcutRow> BuildRows()
    {
        var digits = ShortcutRegistry.All.Where(s => s.Action == ShortcutAction.ShowRailEntry).ToList();
        var rows = new List<ShortcutRow>
        {
            new($"{digits[0].Chord.Display} … {digits[^1].Chord.Display}", "Eintrag an dieser Stelle der Leiste öffnen"),
        };
        rows.AddRange(ShortcutRegistry.All
            .Where(s => s.Action != ShortcutAction.ShowRailEntry)
            .Select(s => new ShortcutRow(s.Chord.Display, s.Description)));
        return rows;
    }
}

/// <summary>One line of the shortcut overview: the keys as a German keyboard labels them, and what they do.</summary>
public sealed record ShortcutRow(string Keys, string Description);
