using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

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
    public void Every_built_in_module_has_exactly_one_shortcut()
    {
        foreach (var module in NavModules.All)
        {
            Assert.Single(ShortcutRegistry.All, s => s.Action == ShortcutAction.ShowModule
                && string.Equals(s.ModuleKey, module, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Module_shortcuts_are_ctrl_and_a_digit_in_the_order_the_rail_shipped_with()
    {
        Assert.Equal("Strg+1", ShortcutRegistry.HintFor(NavModules.Etb));
        Assert.Equal("Strg+4", ShortcutRegistry.HintFor(NavModules.Forces));
        Assert.Equal("Strg+0", ShortcutRegistry.HintFor(NavModules.Contacts));
        Assert.Null(ShortcutRegistry.HintFor(NavModules.Checklist));
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
