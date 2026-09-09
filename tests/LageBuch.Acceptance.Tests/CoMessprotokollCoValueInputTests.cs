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
// directly, as the VM-level tests do) to confirm the implausible-value warning and the #278
// Enter-to-commit KeyBinding both work end-to-end through the actual control.
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
    public void Typing_an_implausible_value_shows_the_warning_before_committing()
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
    public void Pressing_Enter_after_typing_commits_the_value()
    {
        var (window, view, vm, session) = ShowEditor();
        var input = view.GetControl<NumericUpDown>("CoValueInput");

        input.Focus();
        window.KeyTextInput("2500");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.Editor);
        Assert.Equal(2500, EgDwelling(session).CoValue);
    }

    [AvaloniaFact]
    public void Typing_a_plausible_value_shows_no_warning_and_commits_on_Enter()
    {
        var (window, view, vm, session) = ShowEditor();
        var input = view.GetControl<NumericUpDown>("CoValueInput");

        input.Focus();
        window.KeyTextInput("45");
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.Editor!.IsCoImplausible);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(45, EgDwelling(session).CoValue);
    }
}
