using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Acceptance.Tests;

// Drives the real CO-WERT NumericUpDown via headless keyboard input (not by setting Editor.CoValue
// directly, as the VM-level tests do) to confirm the implausible-value warning actually renders as
// the operator types.
//
// There used to be an Enter-to-commit KeyBinding here (closing the sidebar like FERTIG). It was
// removed: typing a value and pressing Enter is one fluid motion, so the sidebar would close in
// the same instant the warning appeared -- defeating the warning's whole point of giving the
// operator a moment to notice and double-check. Pinning that Enter must NOT close the editor is
// exactly what would catch a regression of that mistake.
public class CoMessprotokollCoValueInputTests
{
    private static (Window Window, CoMessprotokollView View, CoMessprotokollViewModel Vm, LocalIncidentSession Session) ShowEditor()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 1, 1); // EG + 1.OG, one apartment each
        var vm = new CoMessprotokollViewModel(session, new FixedClock(), () => { });
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        return (window, view, vm, session);
    }

    private static Dwelling EgDwelling(LocalIncidentSession session) =>
        session.Incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);

    [AvaloniaFact]
    public void Typing_an_implausible_value_shows_the_warning_live()
    {
        var (window, view, vm, _) = ShowEditor();
        var input = view.GetControl<NumericUpDown>("CoValueInput");

        input.Focus();
        window.KeyTextInput("2500");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.Editor);
        Assert.Equal(2500, vm.Editor!.CoValue);
        Assert.True(vm.Editor!.IsCoImplausible);
    }

    [AvaloniaFact]
    public void Pressing_Enter_does_not_close_the_editor()
    {
        // Regression pin for the bug reported after the first version of this PR: an Enter-to-commit
        // shortcut closed the sidebar the instant Enter was pressed, so the warning above was never
        // actually visible to a human typing-then-Enter in one motion. Enter must be a no-op here;
        // only FERTIG/ABBRECHEN may close the editor.
        var (window, view, vm, session) = ShowEditor();
        var input = view.GetControl<NumericUpDown>("CoValueInput");

        input.Focus();
        window.KeyTextInput("2500");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.Editor);
        Assert.True(vm.Editor!.IsCoImplausible);
        Assert.Null(EgDwelling(session).CoValue); // FERTIG was never clicked -- nothing committed
    }

    [AvaloniaFact]
    public void Clicking_FERTIG_after_typing_commits_the_value()
    {
        var (window, view, vm, session) = ShowEditor();
        var input = view.GetControl<NumericUpDown>("CoValueInput");

        input.Focus();
        window.KeyTextInput("2500");
        Dispatcher.UIThread.RunJobs();

        vm.ConfirmEditorCommand.Execute(null); // what the FERTIG Button invokes
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.Editor);
        Assert.Equal(2500, EgDwelling(session).CoValue);
    }
}
