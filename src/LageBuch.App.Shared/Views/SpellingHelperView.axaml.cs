using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Views;

public partial class SpellingHelperView : UserControl
{
    public SpellingHelperView() => InitializeComponent();

    // The clear button hides itself once the word is gone, which would leave focus nowhere; put it
    // back in the word field so the next word can be typed straight away (#963).
    private void OnClearSpellInputClick(object? sender, RoutedEventArgs e) => SpellInput.Focus();
}
