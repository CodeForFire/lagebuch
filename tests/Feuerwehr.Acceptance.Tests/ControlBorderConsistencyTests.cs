using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Feuerwehr.App.Shared.Views;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.Acceptance.Tests;

// Issue #40: the call-sign / name AutoCompleteBox fields wore a light (~60% white) border while
// every other input carried the app's dark Hairline, so a row of look-alike text fields had two
// different edge colours. Avalonia's Fluent AutoCompleteBox pushes its OWN BorderBrush down onto
// its inner TextBox via {TemplateBinding} (Template priority), which outranks the global "TextBox"
// style (Style priority) -- so the app's Hairline token never reached it and the control fell back
// to Fluent's default TextControlBorderBrush (#99FFFFFF in the Dark theme). This pins that an
// AutoCompleteBox presents the same border as the plain TextBoxes it sits among.
public class ControlBorderConsistencyTests
{
    private static Color BorderColor(TemplatedControl c) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(c.BorderBrush).Color;

    [AvaloniaFact]
    public void The_operator_prompt_callsign_field_borders_match_its_textboxes()
    {
        var vm = new OperatorPromptViewModel(callSignOptions: new[] { "FFB 1/40/1", "Aich 42/1" });
        var view = new OperatorPromptView { DataContext = vm };
        var window = new Window { Content = view, Width = 520, Height = 420 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var textBox = view.GetControl<TextBox>("OperatorNameBox");
        var callSign = view.GetControl<AutoCompleteBox>("CallSignBox");

        Assert.Equal(BorderColor(textBox), BorderColor(callSign));
    }
}
