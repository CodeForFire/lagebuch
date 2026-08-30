using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Restricts a <see cref="TextBox"/> to digits — for count fields (GF, Mann, AGT) that were moved
/// off NumericUpDown so a placeholder shows instead of a permanent "0". An input containing any
/// non-digit character is refused wholesale (same contract the NumericUpDown theme had): the good
/// previous value stands rather than a silently mutilated one. Pasted content flows through
/// TextInput in Avalonia too, so one handler covers typing and pasting. An emptied field is
/// legitimate (it means 0 at the view-model level); this behavior only decides what characters may
/// exist, not what they mean.
/// </summary>
public static class IntegerOnly
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(IntegerOnly));

    public static void SetIsEnabled(TextBox target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(TextBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    static IntegerOnly()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            if (e.NewValue is true)
                // Tunneling: must see the input before the TextBox inserts it.
                box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        });
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is TextBox && e.Text is not null && !e.Text.All(char.IsAsciiDigit))
            e.Handled = true;
    }
}
