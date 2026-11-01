using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// A right-click „Kopieren“ for text that sits on a button, where it cannot be selected (#836):
/// set <see cref="TextProperty"/> on the button, and its menu copies that text. A press on a
/// button is the button's, so a SelectableTextBlock inside one would either swallow the click or
/// never select. The Kontakte call button is the case: its text is the number it dials.
///
/// An empty text opens no menu.
/// </summary>
public static class CopyMenu
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Text", typeof(CopyMenu));

    private static readonly ConditionalWeakTable<Control, ContextMenu> Menus = new();

    static CopyMenu()
    {
        TextProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            var hasText = e.NewValue is string { Length: > 0 };
            if (Menus.TryGetValue(control, out var own))
            {
                // Only the menu this put there is taken away again.
                if (!hasText && ReferenceEquals(control.ContextMenu, own))
                {
                    control.ContextMenu = null;
                    Menus.Remove(control);
                }

                return;
            }

            if (hasText && control.ContextMenu is null)
            {
                var copy = new MenuItem { Header = "Kopieren" };
                copy.Click += (_, _) => Copy(control);
                var menu = new ContextMenu { Items = { copy } };
                Menus.Add(control, menu);
                control.ContextMenu = menu;
            }
        });
    }

    public static void SetText(Control target, string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(TextProperty, value);
    }

    public static string? GetText(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(TextProperty);
    }

    // Not awaited, as TextBox's own copy is not: a clipboard the platform refuses keeps what it
    // held, and the next paste shows that.
    private static void Copy(Control control)
    {
        if (GetText(control) is { Length: > 0 } text && TopLevel.GetTopLevel(control)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }
}
