using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// The app-wide shortcuts (#544): set <see cref="IsEnabledProperty"/> on the shell's root, whose
/// DataContext is the <see cref="MainWindowViewModel"/>. The shell is the root on the desktop and
/// on Android alike, so one handler serves both. What a chord does lives in
/// <see cref="ShortcutRegistry"/> and the view models; this only turns a key into a chord.
///
/// The handler is <b>tunneling</b>: Ctrl+Tab reaches the window only on the way down, and keyboard
/// navigation then takes it as a plain Tab and marks it handled (measured in #537). A key the view
/// models turn down is left alone, so it goes on to the focused control as before.
///
/// Nothing fires while focus is in an overlay, an inline panel included: the overlay owns Esc and
/// Enter, and every other key belongs to it until it closes. Alt is never a shortcut here; it stays
/// free for mnemonics (#282).
/// </summary>
public static class GlobalShortcuts
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(GlobalShortcuts));

    // The TopLevel each root hooked and the handler it put there: after detaching, the root no
    // longer knows its TopLevel, so taking the handler off again needs both.
    private static readonly ConditionalWeakTable<Control, Hooked> Handlers = new();

    static GlobalShortcuts()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((root, e) =>
        {
            root.AttachedToVisualTree -= OnAttached;
            root.DetachedFromVisualTree -= OnDetached;
            Unhook(root);
            if (e.NewValue is true)
            {
                root.AttachedToVisualTree += OnAttached;
                root.DetachedFromVisualTree += OnDetached;
                Hook(root);
            }
        });
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control root)
        {
            Hook(root);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control root)
        {
            Unhook(root);
        }
    }

    // On the TopLevel, not on the shell: with nothing focused — just after start, or after the
    // focused control went away — a key is raised on the TopLevel itself and never passes the
    // shell on its way.
    private static void Hook(Control root)
    {
        Unhook(root);
        if (TopLevel.GetTopLevel(root) is { } topLevel)
        {
            EventHandler<KeyEventArgs> handler = (_, e) => OnKeyDown(root, e);
            topLevel.AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel);
            Handlers.AddOrUpdate(root, new Hooked(topLevel, handler));
        }
    }

    private static void Unhook(Control root)
    {
        if (Handlers.TryGetValue(root, out var hooked))
        {
            hooked.TopLevel.RemoveHandler(InputElement.KeyDownEvent, hooked.Handler);
            Handlers.Remove(root);
        }
    }

    public static void SetIsEnabled(Control target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    private static void OnKeyDown(Control root, KeyEventArgs e)
    {
        if (e.Handled || root.DataContext is not MainWindowViewModel shell
            || ChordOf(e.Key, e.KeyModifiers) is not { } chord
            || (e.Source is Visual source && Overlay.IsInOverlay(source)))
        {
            return;
        }

        e.Handled = shell.TryRunShortcut(chord);
    }

    /// <summary>The chord a key press makes, or null when it cannot be a shortcut.</summary>
    internal static KeyChord? ChordOf(Key key, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0 || KeyOf(key) is not { } shortcutKey)
        {
            return null;
        }

        return new KeyChord(
            shortcutKey,
            Ctrl: (modifiers & KeyModifiers.Control) != 0,
            Shift: (modifiers & KeyModifiers.Shift) != 0);
    }

    // The number row and the keypad both count: Strg+4 is the same shortcut either way.
    private static ShortcutKey? KeyOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ShortcutKey.D0 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => ShortcutKey.D0 + (key - Key.NumPad0),
        Key.N => ShortcutKey.N,
        Key.Tab => ShortcutKey.Tab,
        Key.F1 => ShortcutKey.F1,
        Key.F9 => ShortcutKey.F9,
        _ => null,
    };

    private sealed record Hooked(TopLevel TopLevel, EventHandler<KeyEventArgs> Handler);
}
