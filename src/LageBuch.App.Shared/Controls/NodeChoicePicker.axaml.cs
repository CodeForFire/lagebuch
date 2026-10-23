using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A button naming where something sits, with a flyout of <see cref="NodeChoice"/>s to move it.
/// Bind <see cref="Choices"/> and <see cref="Label"/>; the button is disabled with the control.
/// </summary>
public partial class NodeChoicePicker : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<NodeChoice>?> ChoicesProperty =
        AvaloniaProperty.Register<NodeChoicePicker, IReadOnlyList<NodeChoice>?>(nameof(Choices));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<NodeChoicePicker, string?>(nameof(Label));

    public NodeChoicePicker() => InitializeComponent();

    public IReadOnlyList<NodeChoice>? Choices
    {
        get => GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    // A Button inside a Flyout does not dismiss it, and the row owning the picker is kept across
    // refreshes (#294), so nothing else would close it. The command itself runs from the Button.
    private void OnChoiceClick(object? sender, RoutedEventArgs e) => PickerButton.Flyout?.Hide();
}
