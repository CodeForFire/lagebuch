using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Shared.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;

namespace Feuerwehr.Acceptance.Tests;

// Every numeric input in this app is a count: Personen, Atemschutzgeräteträger, Minuten, bar.
// None has a fractional meaning, and the controls must not pretend otherwise. Left at the
// NumericUpDown defaults, typing "3.5" left the control showing 3,5 while the int-typed view-model
// property rounded it to 4 -- the number on screen was not the number written into the Einsatz
// record, and it rounded up, inventing a Feuerwehrmann in the Gesamtstärke.
public class NumericInputTests
{
    private static (Window Window, ForcesView View, IncidentWorkspaceViewModel Vm) ShowForces()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var view = new ForcesView { DataContext = vm.Forces };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static TextBox Type(Window window, ForcesView view, NumericUpDown control, string text)
    {
        var inner = control.GetVisualDescendants().OfType<TextBox>().First();
        inner.Focus();
        inner.SelectAll();
        window.KeyTextInput(text);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Tab away, as a user filling the row does. NumericUpDown reformats the text on lost
        // focus, so this is the point at which what is shown must equal what is held.
        view.GetControl<TextBox>("NotesBox").Focus();
        Dispatcher.UIThread.RunJobs();
        return inner;
    }

    [AvaloniaTheory]
    [InlineData("3.5")]
    [InlineData("3,5")]
    [InlineData("0.9")]
    [InlineData("abc")]
    public void A_fractional_personnel_entry_is_refused_and_the_previous_value_stands(string typed)
    {
        var (window, view, vm) = ShowForces();
        var box = view.GetControl<NumericUpDown>("PersonnelBox");

        Type(window, view, box, "9");
        Assert.Equal(9, vm.Forces.NewPersonnelCount);

        var inner = Type(window, view, box, typed);

        // Rejected outright -- the good value stands rather than being rounded or zeroed.
        Assert.Equal(9m, box.Value);
        Assert.Equal(9, vm.Forces.NewPersonnelCount);
        Assert.Equal("9", inner.Text);
    }

    [AvaloniaTheory]
    [InlineData("2.5")]
    [InlineData("2,5")]
    public void A_fractional_agt_entry_is_refused(string typed)
    {
        var (window, view, vm) = ShowForces();
        var box = view.GetControl<NumericUpDown>("ScbaBox");

        Type(window, view, box, "4");
        var inner = Type(window, view, box, typed);

        Assert.Equal(4m, box.Value);
        Assert.Equal(4, vm.Forces.NewScbaCount);
        Assert.Equal("4", inner.Text);
    }

    [AvaloniaFact]
    public void Whole_numbers_still_go_through_untouched()
    {
        var (window, view, vm) = ShowForces();
        var box = view.GetControl<NumericUpDown>("PersonnelBox");

        var inner = Type(window, view, box, "12");

        Assert.Equal(12, vm.Forces.NewPersonnelCount);
        Assert.Equal(12m, box.Value);
        Assert.Equal("12", inner.Text);
    }

    [AvaloniaFact]
    public void The_integer_rule_is_a_theme_default_not_a_per_control_opt_in()
    {
        // Set once on the shared NumericUpDown style, so the Atemschutz inputs (bar, Minuten) and
        // any input added later inherit it. Pinning that here stops the fix from being quietly
        // undone by a new control that forgets to opt in.
        var (_, view, _) = ShowForces();

        foreach (var name in new[] { "PersonnelBox", "ScbaBox" })
        {
            var box = view.GetControl<NumericUpDown>(name);
            Assert.Equal(System.Globalization.NumberStyles.Integer, box.ParsingNumberStyle);
            Assert.Equal("0", box.FormatString);
        }
    }
}

// Status and Bemerkung move constantly during an Einsatz -- a unit goes from Alarmiert to Im
// Einsatz -- so the Kräfte grid has to offer them as controls, not as printed text. This pins the
// affordance itself: the columns were DataGridTextColumn in a grid marked IsReadOnly, which is why
// neither could be changed once the row was added.
public class ForcesGridEditingTests
{
    private static (ForcesView View, IncidentWorkspaceViewModel Vm) ShowForces(out LocalIncidentSession session)
    {
        session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());
        var vm = new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(),
            WorkspaceRenderHelper.MasterData(), new FakeDialogs(), new NoopAlarmService());
        var view = new ForcesView { DataContext = vm.Forces };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, vm);
    }

    private static DataGridColumn Column(ForcesView view, string header) =>
        view.GetControl<DataGrid>("ForcesGrid").Columns.First(c => (string)c.Header == header);

    [AvaloniaFact]
    public void Status_and_bemerkung_are_editable_columns()
    {
        var (view, _) = ShowForces(out _);

        Assert.IsType<DataGridTemplateColumn>(Column(view, "STATUS"));
        Assert.IsType<DataGridTemplateColumn>(Column(view, "BEMERKUNG"));
    }

    [AvaloniaFact]
    public void The_descriptive_columns_stay_read_only_text()
    {
        // Which Feuerwehr, how many people, how many AGT are facts about what was alarmed.
        var (view, _) = ShowForces(out _);

        foreach (var header in new[] { "FEUERWEHR", "FUNKRUFNAME", "STÄRKE", "AGT" })
            Assert.IsType<DataGridTextColumn>(Column(view, header));
    }

    [AvaloniaFact]
    public void Editing_status_on_a_row_reaches_the_incident()
    {
        var (view, vm) = ShowForces(out var session);
        vm.Forces.NewBrigade = "FFB Wache 1";
        vm.Forces.NewPersonnelCount = 9;
        vm.Forces.NewStatus = "Alarmiert";
        vm.Forces.AddForceCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(vm.Forces.Forces);
        row.Status = "Im Einsatz";
        row.Notes = "über DLK angefordert";
        Dispatcher.UIThread.RunJobs();

        var unit = Assert.Single(session.Incident.Forces);
        Assert.Equal("Im Einsatz", unit.Status);
        Assert.Equal("über DLK angefordert", unit.Notes);
    }
}
