using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Opens an <see cref="AutoCompleteBox"/>'s suggestion dropdown as soon as it gets focus or is
/// clicked, instead of waiting for the first keystroke (#259). Every AutoCompleteBox in this app
/// is configured with MinimumPrefixLength="0" (see Styles.axaml), so it is already willing to show
/// its full suggestion list for an empty string — the control just never asks to, because Avalonia
/// only re-evaluates/opens the popup when the text changes, not on focus or a click.
///
/// Wired up once via a Setter on the shared "AutoCompleteBox" style rather than per-view XAML, so
/// every suggestion field (Funktion, Name, Funkrufname, Truppmann, Fahrzeug, Aufgabe, ...) opens
/// the same way without opting in one by one.
/// </summary>
public static class OpenDropDownOnFocus
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, bool>("IsEnabled", typeof(OpenDropDownOnFocus));

    public static void SetIsEnabled(AutoCompleteBox target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(AutoCompleteBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    static OpenDropDownOnFocus()
    {
        IsEnabledProperty.Changed.AddClassHandler<AutoCompleteBox>((box, e) =>
        {
            box.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
            box.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            if (e.NewValue is true)
            {
                box.AddHandler(InputElement.GotFocusEvent, OnGotFocus);

                // Tunnel + handledEventsToo: the inner TextBox handles PointerPressed itself (to
                // place the caret) and marks it handled, which would otherwise stop a bubbling
                // handler from ever seeing the click — mirrors EnterSubmit's rationale.
                box.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            }
        });
    }

    private static void OnGotFocus(object? sender, RoutedEventArgs e) => Open(sender);

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e) => Open(sender);

    private static void Open(object? sender)
    {
        if (sender is AutoCompleteBox { IsDropDownOpen: false } box)
        {
            box.IsDropDownOpen = true;
        }
    }
}
