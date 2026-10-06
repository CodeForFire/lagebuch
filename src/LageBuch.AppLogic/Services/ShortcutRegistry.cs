using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Every global shortcut, in the order the F1 overview lists them (#544). The key handler, the
/// rail's key hints and the overview all read this one table, so none of them can drift.
/// </summary>
/// <remarks>
/// A module's shortcut is bound to its key, never to its rail position: the rail comes from the
/// Stammdaten Navigation and the Einsatz's own Checklisten, so "the fourth tab" differs between
/// Feuerwehren and incidents. The digits follow the order the rail shipped with
/// (<see cref="NavModules.All"/>), Ctrl+0 being the tenth. Checklisten are user-named and have no
/// fixed key; Ctrl+Tab reaches them.
/// </remarks>
public static class ShortcutRegistry
{
    private static readonly ShortcutKey[] Digits =
    {
        ShortcutKey.D1, ShortcutKey.D2, ShortcutKey.D3, ShortcutKey.D4, ShortcutKey.D5,
        ShortcutKey.D6, ShortcutKey.D7, ShortcutKey.D8, ShortcutKey.D9, ShortcutKey.D0,
    };

    public static IReadOnlyList<Shortcut> All { get; } = Build();

    /// <summary>The shortcut bound to <paramref name="chord"/>; null when there is none.</summary>
    public static Shortcut? Find(KeyChord chord) => All.FirstOrDefault(s => s.Chord == chord);

    /// <summary>The key hint for a built-in module's rail tab; null for a Checkliste or an unknown key.</summary>
    public static string? HintFor(string moduleKey) =>
        All.FirstOrDefault(s => s.Action == ShortcutAction.ShowModule
            && string.Equals(s.ModuleKey, moduleKey, StringComparison.Ordinal))?.Chord.Display;

    private static List<Shortcut> Build()
    {
        var shortcuts = NavModules.All
            .Select((module, i) => new Shortcut(
                new KeyChord(Digits[i], Ctrl: true),
                ShortcutAction.ShowModule,
                $"{ModuleLabels.Header(module)} öffnen",
                module))
            .ToList();

        shortcuts.Add(new Shortcut(new KeyChord(ShortcutKey.Tab, Ctrl: true), ShortcutAction.NextModule, "Nächstes Modul"));
        shortcuts.Add(new Shortcut(new KeyChord(ShortcutKey.Tab, Ctrl: true, Shift: true), ShortcutAction.PreviousModule, "Vorheriges Modul"));
        shortcuts.Add(new Shortcut(new KeyChord(ShortcutKey.N, Ctrl: true), ShortcutAction.NewEtbEntry, "Neuer ETB-Eintrag"));
        shortcuts.Add(new Shortcut(new KeyChord(ShortcutKey.F9), ShortcutAction.MostUrgentWarning, "Zur dringendsten Meldung"));
        shortcuts.Add(new Shortcut(new KeyChord(ShortcutKey.F1), ShortcutAction.ShowOverview, "Tastenkürzel anzeigen"));
        return shortcuts;
    }
}

/// <summary>
/// The keys a global shortcut can use (#544). Deliberately a closed list of its own rather than
/// Avalonia's <c>Key</c>: AppLogic has no Avalonia reference, and a key that is not here cannot
/// be bound by accident — there is no member for A, C, V, X, Y or Z, which the text boxes own.
/// </summary>
public enum ShortcutKey
{
    D0,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,
    N,
    Tab,
    F1,
    F9,
}

/// <summary>A key with the modifiers held for it. Alt is not a modifier here: it stays free for mnemonics (#282).</summary>
public sealed record KeyChord(ShortcutKey Key, bool Ctrl = false, bool Shift = false)
{
    /// <summary>How the chord is written on screen, with the labels a German keyboard carries.</summary>
    public string Display =>
        (Ctrl ? "Strg+" : string.Empty) + (Shift ? "Umschalt+" : string.Empty) + KeyLabel(Key);

    private static string KeyLabel(ShortcutKey key) => key switch
    {
        >= ShortcutKey.D0 and <= ShortcutKey.D9 => ((int)key - (int)ShortcutKey.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };
}

/// <summary>What a global shortcut does.</summary>
public enum ShortcutAction
{
    /// <summary>Open the module named by <see cref="Shortcut.ModuleKey"/>.</summary>
    ShowModule,

    /// <summary>The next rail entry as shown, wrapping.</summary>
    NextModule,

    /// <summary>The previous rail entry as shown, wrapping.</summary>
    PreviousModule,

    /// <summary>Open the ETB with the caret in VON.</summary>
    NewEtbEntry,

    /// <summary>Go to the most urgent open warning (<see cref="WarningKind"/>).</summary>
    MostUrgentWarning,

    /// <summary>Show the shortcut overview.</summary>
    ShowOverview,
}

/// <summary>One global shortcut: its chord, what it does, and the German line the overview shows for it.</summary>
public sealed record Shortcut(KeyChord Chord, ShortcutAction Action, string Description, string? ModuleKey = null);
