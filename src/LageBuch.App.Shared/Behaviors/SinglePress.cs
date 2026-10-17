using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Ignores the second press of a double-click or double-tap. The Atemschutz row puts each state's
/// next button in the slot the previous one just left — IM EINSATZ becomes DRUCK and RÜCKZUG,
/// RÜCKZUG becomes ABGENOMMEN — so the second click of a double-click lands on a button the first
/// click made appear, and fires a step nobody chose (#601, #630).
///
/// Avalonia counts clicks by position and time, not by the control under the pointer, so that
/// second press arrives with <see cref="PointerPressedEventArgs.ClickCount"/> above one, whichever
/// button it hits, from a mouse or a touch screen. The handler is <b>tunneling</b> so it marks the press
/// handled before the button sees it: the button then never becomes pressed, and the release
/// raises no click.
/// </summary>
public static class SinglePress
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<InputElement, bool>("IsEnabled", typeof(SinglePress));

    public static void SetIsEnabled(InputElement target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(InputElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    static SinglePress()
    {
        IsEnabledProperty.Changed.AddClassHandler<InputElement>((input, e) =>
        {
            input.RemoveHandler(InputElement.PointerPressedEvent, OnPreviewPointerPressed);
            if (e.NewValue is true)
            {
                input.AddHandler(InputElement.PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
            }
        });
    }

    private static void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            e.Handled = true;
        }
    }
}
