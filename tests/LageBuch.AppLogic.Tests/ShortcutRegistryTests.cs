using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

// The registry is the one table the key handler, the rail hints and the F1 overview all read
// (#544), so its rules are pinned here rather than in each of them.
public class ShortcutRegistryTests
{
    [Fact]
    public void No_two_shortcuts_share_a_chord()
    {
        var chords = ShortcutRegistry.All.Select(s => s.Chord).ToList();

        Assert.Equal(chords.Count, chords.Distinct().Count());
    }

    [Fact]
    public void Digit_shortcuts_are_ctrl_1_to_0_for_the_first_ten_rail_entries()
    {
        Assert.Equal("Strg+1", ShortcutRegistry.HintFor(0));
        Assert.Equal("Strg+4", ShortcutRegistry.HintFor(3));
        Assert.Equal("Strg+0", ShortcutRegistry.HintFor(9));
        Assert.Null(ShortcutRegistry.HintFor(10));
        Assert.Equal(
            Enumerable.Range(0, 10).Cast<int?>(),
            ShortcutRegistry.All.Where(s => s.Action == ShortcutAction.ShowRailEntry).Select(s => s.RailIndex));
    }

    [Fact]
    public void The_overview_lists_the_digit_shortcuts_as_one_row()
    {
        var rows = new ShortcutOverviewViewModel().Rows;

        Assert.Equal("Strg+1 … Strg+0", rows[0].Keys);
        Assert.DoesNotContain(rows, r => r.Keys == "Strg+4");
        Assert.Equal(ShortcutRegistry.All.Count - 9, rows.Count);
    }

    [Fact]
    public void No_shortcut_takes_a_text_editing_key()
    {
        // Ctrl+A/C/V/X/Z/Y belong to the text boxes. The enum cannot even name them, so this
        // checks that it stays that way.
        var editing = new[] { "A", "C", "V", "X", "Z", "Y" };

        Assert.DoesNotContain(ShortcutRegistry.All, s => s.Chord.Ctrl && editing.Contains(s.Chord.Key.ToString()));
    }

    [Fact]
    public void Chords_read_the_way_a_german_keyboard_labels_them()
    {
        Assert.Equal("Strg+Umschalt+Tab", new KeyChord(ShortcutKey.Tab, Ctrl: true, Shift: true).Display);
        Assert.Equal("Strg+N", new KeyChord(ShortcutKey.N, Ctrl: true).Display);
        Assert.Equal("F9", new KeyChord(ShortcutKey.F9).Display);
    }

    [Fact]
    public void Find_returns_the_shortcut_registered_for_a_chord_and_null_for_any_other()
    {
        Assert.Equal(ShortcutAction.NewEtbEntry, ShortcutRegistry.Find(new KeyChord(ShortcutKey.N, Ctrl: true))?.Action);
        Assert.Null(ShortcutRegistry.Find(new KeyChord(ShortcutKey.N)));
    }
}
