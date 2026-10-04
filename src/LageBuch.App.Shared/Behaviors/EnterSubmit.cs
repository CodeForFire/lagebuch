using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Runs a command when Enter is pressed in an input — and, in an <see cref="AutoCompleteBox"/>,
/// only while its suggestion dropdown is closed. With the dropdown open, Enter belongs to the list
/// (accept the highlighted suggestion); submitting the surrounding form then would fire two actions
/// from one keypress — e.g. picking a Truppmann would also create the Trupp. The Atemschutz row's
/// Druck field uses it too (#539), so a Druckkontrolle is typed and entered like any other value.
///
/// A plain <c>KeyBinding Gesture="Enter"</c> cannot make that distinction, so it is replaced by
/// this attached command. The handler is <b>tunneling</b> on purpose: it must read
/// <see cref="AutoCompleteBox.IsDropDownOpen"/> before the box processes Enter and closes the list.
/// </summary>
public static class EnterSubmit
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<InputElement, ICommand?>("Command", typeof(EnterSubmit));

    public static void SetCommand(InputElement target, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CommandProperty, value);
    }

    public static ICommand? GetCommand(InputElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(CommandProperty);
    }

    static EnterSubmit()
    {
        CommandProperty.Changed.AddClassHandler<InputElement>((input, e) =>
        {
            input.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
            if (e.NewValue is ICommand)
            {
                input.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            }
        });
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not InputElement input || input is AutoCompleteBox { IsDropDownOpen: true })
        {
            return;
        }

        var command = GetCommand(input);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
