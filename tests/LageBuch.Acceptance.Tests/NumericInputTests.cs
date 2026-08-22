using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

using LageBuch.AppLogic.Services;

namespace LageBuch.Acceptance.Tests;

// Every numeric input about people (GF, Mann, AGT) is a plain TextBox with an IntegerOnly filter
// since the #76 rework: a spinner next to every field was visual noise, and a permanent "0" kept
// the placeholder from ever showing. The behavior swallows non-digit characters before they reach
// the text -- typing and pasting both flow through TextInput. An emptied field means 0 at the
// view-model level (nullable ints). Measurement inputs elsewhere (bar, Minuten) stay NumericUpDown.
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

    private static TextBox Type(Window window, ForcesView view, TextBox box, string text)
    {
        box.Focus();
        box.SelectAll();
        window.KeyTextInput(text);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Tab away, as a user filling the row does.
        view.GetControl<TextBox>("NotesBox").Focus();
        Dispatcher.UIThread.RunJobs();
        return box;
    }

    [AvaloniaTheory]
    [InlineData("3.5")]
    [InlineData("3,5")]
    [InlineData("abc")]
    [InlineData("a3b5")]
    public void A_non_digit_entry_is_refused_and_the_previous_value_stands(string typed)
    {
        var (window, view, vm) = ShowForces();
        var box = view.GetControl<TextBox>("MannschaftBox");

        Type(window, view, box, "9");
        Assert.Equal(9, vm.Forces.NewMannschaftCount);

        var inner = Type(window, view, box, typed);

        // Refused wholesale -- the good value stands rather than a mutilated "35".
        Assert.Equal(9, vm.Forces.NewMannschaftCount);
        Assert.Equal("9", inner.Text);
    }

    [AvaloniaTheory]
    [InlineData("2.5")]
    [InlineData("2,5")]
    public void A_non_digit_agt_entry_is_refused(string typed)
    {
        var (window, view, vm) = ShowForces();
        var box = view.GetControl<TextBox>("ScbaBox");

        Type(window, view, box, "4");
        var inner = Type(window, view, box, typed);

        Assert.Equal(4, vm.Forces.NewScbaCount);
        Assert.Equal("4", inner.Text);
    }

    [AvaloniaFact]
    public void Whole_numbers_still_go_through_untouched()
    {
        var (window, view, vm) = ShowForces();
        var box = view.GetControl<TextBox>("MannschaftBox");

        var inner = Type(window, view, box, "12");

        Assert.Equal(12, vm.Forces.NewMannschaftCount);
        Assert.Equal("12", inner.Text);
    }

    [AvaloniaFact]
    public void The_personnel_fields_are_textboxes_with_the_integer_filter_not_spinners()
    {
        // Pins the #76-rework affordance: GF/Mann/AGT are plain fields with IntegerOnly attached,
        // so the placeholder stays visible while the value is unset.
        var (_, view, _) = ShowForces();

        foreach (var name in new[] { "OfficerBox", "MannschaftBox", "ScbaBox" })
        {
            var box = view.GetControl<TextBox>(name);
            Assert.True(LageBuch.App.Shared.Behaviors.IntegerOnly.GetIsEnabled(box),
                $"{name} must carry IntegerOnly.");
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
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(),
            WorkspaceRenderHelper.MasterData(), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
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
        // Which LageBuch, how many AGT are facts about what was alarmed; since #76 the Stärke
        // column is a template (text plus the correction editor), so it is pinned separately.
        var (view, _) = ShowForces(out _);

        foreach (var header in new[] { "FEUERWEHR", "FUNKRUFNAME", "AGT" })
            Assert.IsType<DataGridTextColumn>(Column(view, header));
        Assert.IsType<DataGridTemplateColumn>(Column(view, "STÄRKE"));
    }

    [AvaloniaFact]
    public void Editing_status_on_a_row_reaches_the_incident()
    {
        var (view, vm) = ShowForces(out var session);
        vm.Forces.NewBrigade = "FFB Wache 1";
        vm.Forces.NewMannschaftCount = 9;
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
