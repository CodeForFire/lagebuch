using Avalonia;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Puts the <c>alarm</c> class on an element from a bindable flag. A <c>DataGridRow</c> is made by
/// the grid, not declared in XAML, so <c>Classes.alarm="{Binding …}"</c> has nowhere to go; a
/// row style can still set this attached property from the row's item (#669).
/// </summary>
public static class AlarmClass
{
    public static readonly AttachedProperty<bool> IsOnProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, bool>("IsOn", typeof(AlarmClass));

    public static void SetIsOn(StyledElement target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsOnProperty, value);
    }

    public static bool GetIsOn(StyledElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsOnProperty);
    }

    static AlarmClass()
    {
        IsOnProperty.Changed.AddClassHandler<StyledElement>((element, e) =>
            element.Classes.Set("alarm", e.GetNewValue<bool>()));
    }
}
