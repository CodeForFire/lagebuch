using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Feuerwehr.App.Shared.Behaviors;

/// <summary>
/// Runs a command when Enter is pressed in an <see cref="AutoCompleteBox"/> — but only while its
/// suggestion dropdown is closed. With the dropdown open, Enter belongs to the list (accept the
/// highlighted suggestion); submitting the surrounding form then would fire two actions from one
/// keypress — e.g. picking a Truppmann would also create the Trupp.
///
/// A plain <c>KeyBinding Gesture="Enter"</c> cannot make that distinction, so it is replaced by
/// this attached command. The handler is <b>tunneling</b> on purpose: it must read
/// <see cref="AutoCompleteBox.IsDropDownOpen"/> before the box processes Enter and closes the list.
/// </summary>
public static class EnterSubmit
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, ICommand?>("Command", typeof(EnterSubmit));

    public static void SetCommand(AutoCompleteBox target, ICommand? value) =>
        target.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(AutoCompleteBox target) =>
        target.GetValue(CommandProperty);

    static EnterSubmit()
    {
        CommandProperty.Changed.AddClassHandler<AutoCompleteBox>((box, e) =>
        {
            box.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
            if (e.NewValue is ICommand)
                box.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        });
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not AutoCompleteBox box || box.IsDropDownOpen)
            return;
        var command = GetCommand(box);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
