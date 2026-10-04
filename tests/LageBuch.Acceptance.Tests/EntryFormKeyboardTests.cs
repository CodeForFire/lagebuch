using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #540, contract 2 of #545: in every entry dock Enter submits from any single-line field, and focus
// then goes back to the form's first field -- or, when validation refuses the entry, to the first
// field it marked. Each form is filled by keyboard alone; no test sets a view model property it
// could have typed. Each test puts focus in the form itself: a view's attach-time focus does not
// land under the headless host, and #542 reworks when a view may take it anyway.
public class EntryFormKeyboardTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void FocusByTab(Window window, Control control)
    {
        control.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    // VON is the first field and is cleared like AN: an entry is VON, Tab, AN, Tab, EINTRAG, Enter,
    // and the next one starts at VON again -- no Shift+Tab anywhere.
    [AvaloniaFact]
    public void Etb_three_entries_in_a_row_without_shift_tab()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var from = Named<AutoCompleteBox>(window, "FromBox");
        FocusByTab(window, from);

        foreach (var (sender, entry) in new[]
                 {
                     ("Florian Testort 40/1", "Erste Lagemeldung"),
                     ("Florian Testort 11/1", "Wasserversorgung steht"),
                     ("Florian Testort 40/1", "Nachforderung RTW"),
                 })
        {
            window.Type(sender);
            window.Tab();
            window.Tab(); // AN stays empty
            window.Type(entry);
            window.Press(PhysicalKey.Enter);
            window.AssertFocused(from);
            Assert.True(string.IsNullOrEmpty(from.Text)); // not sticky
        }

        Assert.Equal(
            new[] { "Nachforderung RTW", "Wasserversorgung steht", "Erste Lagemeldung" },
            vm.Etb.Entries.Take(3).Select(e => e.Text));
        Assert.Equal(
            new[] { "Florian Testort 40/1", "Florian Testort 11/1", "Florian Testort 40/1" },
            vm.Etb.Entries.Take(3).Select(e => e.From));
    }

    [AvaloniaFact]
    public void Etb_enter_from_von_without_eintrag_is_refused_and_focus_goes_to_eintrag()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var from = Named<AutoCompleteBox>(window, "FromBox");
        FocusByTab(window, from);
        window.Type("Florian Testort 40/1");
        from.IsDropDownOpen = false; // nothing highlighted

        window.Press(PhysicalKey.Enter);

        Assert.DoesNotContain(vm.Etb.Entries, e => e.From == "Florian Testort 40/1");
        Assert.Equal("Florian Testort 40/1", from.Text); // a refusal keeps what was typed
        window.AssertFocused(Named<TextBox>(window, "EtbTextBox"));
    }

    [AvaloniaFact]
    public void Kraefte_enter_from_the_last_field_submits_and_focus_goes_to_fahrzeug()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        var vehicle = Named<ComboBox>(window, "VehicleBox");
        FocusByTab(window, vehicle);

        window.Tab();
        window.Type("FF Nachbarort");
        window.Tab();
        window.Type("Florian Nachbarort 40/1");
        window.Tab();
        window.Tab();
        window.Type("1");
        window.Tab();
        window.Type("5");
        FocusByTab(window, Named<TextBox>(window, "NotesBox"));
        window.Type("über B2");
        window.Press(PhysicalKey.Enter);

        var unit = Assert.Single(vm.Forces.Forces);
        Assert.Equal("Florian Nachbarort 40/1", unit.CallSign);
        Assert.Equal(6, unit.TotalCount);
        window.AssertFocused(vehicle);
    }

    [AvaloniaFact]
    public void Kraefte_vehicle_path_submits_on_enter()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        var vehicle = Named<ComboBox>(window, "VehicleBox");
        FocusByTab(window, vehicle);

        window.Press(PhysicalKey.ArrowDown); // a closed ComboBox steps its selection
        Assert.NotNull(vm.Forces.SelectedVehicle);
        window.Press(PhysicalKey.Enter);

        Assert.Single(vm.Forces.Forces);
        Assert.False(vehicle.IsDropDownOpen); // Enter submitted, it did not open the list
        window.AssertFocused(vehicle);
    }

    [AvaloniaFact]
    public void Kraefte_two_vehicles_from_one_feuerwehr_without_retyping_it()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        FocusByTab(window, Named<ComboBox>(window, "VehicleBox"));
        window.Tab();
        window.Type("FF Nachbarort");
        window.Tab();
        window.Type("Florian Nachbarort 40/1");
        TypeMannschaft(window, "6");
        window.Press(PhysicalKey.Enter);

        window.Tab(); // FAHRZEUG -> FEUERWEHR, still filled in and selected
        var brigade = Named<TextBox>(window, "BrigadeBox");
        window.AssertFocused(brigade);
        Assert.Equal("FF Nachbarort", brigade.Text);
        Assert.Equal("FF Nachbarort", brigade.SelectedText);
        window.Tab();
        window.Type("Florian Nachbarort 11/1");
        TypeMannschaft(window, "9");
        window.Press(PhysicalKey.Enter);

        Assert.Equal(new[] { "FF Nachbarort", "FF Nachbarort" }, vm.Forces.Forces.Select(f => f.Brigade));
    }

    // A unit with no Stärke is refused, and focus goes to ZF -- see the refusal test below.
    private static void TypeMannschaft(Window window, string count)
    {
        FocusByTab(window, Named<TextBox>(window, "MannschaftBox"));
        window.Type(count);
    }

    [AvaloniaFact]
    public void Kraefte_a_unit_without_staerke_is_refused_and_focus_goes_to_it()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        FocusByTab(window, Named<TextBox>(window, "BrigadeBox"));
        window.Type("FF Nachbarort");
        window.Tab();
        window.Type("Florian Nachbarort 40/1");

        window.Press(PhysicalKey.Enter);

        Assert.Empty(vm.Forces.Forces);
        window.AssertFocused(Named<TextBox>(window, "ZugfuehrerBox"));
    }

    [AvaloniaFact]
    public void Enter_in_an_open_combobox_picks_and_does_not_submit()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        FocusByTab(window, Named<ComboBox>(window, "VehicleBox"));
        window.Tab();
        window.Type("FF Nachbarort");
        window.Tab();
        window.Type("Florian Nachbarort 40/1");
        var status = Named<ComboBox>(window, "StatusBox");
        FocusByTab(window, status);
        window.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.True(status.IsDropDownOpen);
        window.Press(PhysicalKey.ArrowDown);

        window.Press(PhysicalKey.Enter);

        Assert.Empty(vm.Forces.Forces);
        Assert.False(status.IsDropDownOpen);
        Assert.NotNull(vm.Forces.NewStatus);
    }

    [AvaloniaFact]
    public void Atemschutz_enter_from_the_last_field_submits_and_focus_goes_to_funkrufname()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ATEMSCHUTZ");
        var callSign = Named<AutoCompleteBox>(window, "CallSignBox");
        FocusByTab(window, callSign);

        window.Type("Florian Testort 40/1");
        callSign.IsDropDownOpen = false;
        FocusByTab(window, Named<ComboBox>(window, "TruppTypeBox"));
        window.Press(PhysicalKey.ArrowDown);
        FocusByTab(window, Named<AutoCompleteBox>(window, "TruppfuehrerBox"));
        window.Type("Erika Testfrau");
        FocusByTab(window, Named<AutoCompleteBox>(window, "TruppmannBox"));
        window.Type("Max Testmann");
        var interval = window.GetVisualDescendants().OfType<NumericUpDown>()
            .Last(n => n.FindAncestorOfType<WrapPanel>()?.Name == "RegistrationDock");
        FocusByTab(window, interval);
        window.Press(PhysicalKey.Enter);

        var trupp = Assert.Single(vm.Scba.Trupps);
        Assert.Equal("Florian Testort 40/1", trupp.CallSign);
        window.AssertFocused(callSign);
    }

    [AvaloniaFact]
    public void A_refused_submit_focuses_the_first_invalid_field()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ATEMSCHUTZ");
        FocusByTab(window, Named<ComboBox>(window, "TruppTypeBox"));
        window.Press(PhysicalKey.ArrowDown);
        FocusByTab(window, Named<AutoCompleteBox>(window, "TruppmannBox"));
        window.Type("Max Testmann");
        Named<AutoCompleteBox>(window, "TruppmannBox").IsDropDownOpen = false;

        window.Press(PhysicalKey.Enter); // no Truppführer

        Assert.Empty(vm.Scba.Trupps);
        window.AssertFocused(Named<AutoCompleteBox>(window, "TruppfuehrerBox"));
    }

    [AvaloniaFact]
    public void Rollen_enter_from_the_last_field_submits_and_focus_goes_to_funktion()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        var role = Named<AutoCompleteBox>(window, "RoleBox");
        FocusByTab(window, role);

        window.Type("Lageerkundung");
        role.IsDropDownOpen = false;
        FocusByTab(window, Named<AutoCompleteBox>(window, "PersonNameBox"));
        window.Type("Erika Testfrau");
        FocusByTab(window, Named<TextBox>(window, "PhoneBox"));
        window.Type("0170 0000000");
        window.Press(PhysicalKey.Enter);

        var row = Assert.Single(vm.Roles.Roles);
        Assert.Equal("Erika Testfrau", row.PersonName);
        window.AssertFocused(role);
    }

    // The handover panel sits beside the dock with its own primary command; its Enter must not
    // also submit the empty form behind it, which would mark FUNKTION and pull focus there.
    [AvaloniaFact]
    public void Enter_in_the_handover_panel_does_not_submit_the_role_form()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        vm.Roles.NewRole = "EL";
        vm.Roles.NewPersonName = "Erika Testfrau";
        vm.Roles.AddRoleCommand.Execute(null);
        vm.Roles.Roles[0].BeginTransferCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.AssertFocused(Named<AutoCompleteBox>(window, "TransferPersonNameBox"));
        window.Type("Max Testmann");
        Named<AutoCompleteBox>(window, "TransferPersonNameBox").IsDropDownOpen = false;

        window.Press(PhysicalKey.Enter);

        Assert.False(vm.Roles.IsTransferring);
        Assert.Null(vm.Roles.NewRoleError);
        Assert.Contains(vm.Roles.Roles, r => r.PersonName == "Max Testmann");
    }

    // WICHTIGKEIT is the first field; it, DRINGLICHKEIT and ZUGETEILT keep their values between
    // tasks, so the next task is Tab x4, the text, Enter.
    [AvaloniaFact]
    public void Aufgaben_enter_from_the_last_field_submits_and_focus_goes_to_wichtigkeit()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUFGABEN");
        var importance = Named<ComboBox>(window, "ImportanceBox");
        FocusByTab(window, importance);
        window.Press(PhysicalKey.ArrowDown); // a closed ComboBox steps its selection
        var picked = vm.Tasks.NewImportance;
        window.Tab();
        window.Tab();
        window.Tab();
        window.Tab();
        window.AssertFocused(Named<TextBox>(window, "TaskNewTextBox"));
        window.Type("Hydrantenplan holen");

        window.Press(PhysicalKey.Enter);

        Assert.Equal("Hydrantenplan holen", Assert.Single(vm.Tasks.Rows).Text);
        window.AssertFocused(importance);
        Assert.Equal(picked, vm.Tasks.NewImportance); // kept for the next task
    }

    [AvaloniaFact]
    public void Aufgaben_enter_from_the_timer_submits_and_focus_goes_to_wichtigkeit()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUFGABEN");
        FocusByTab(window, Named<TextBox>(window, "TaskNewTextBox"));
        window.Type("Zufahrt freihalten");
        window.ShiftTab(); // TIMER

        window.Press(PhysicalKey.Enter);

        Assert.Equal("Zufahrt freihalten", Assert.Single(vm.Tasks.Rows).Text);
        window.AssertFocused(Named<ComboBox>(window, "ImportanceBox"));
    }

    [AvaloniaFact]
    public void Beteiligte_enter_from_the_last_field_submits_and_focus_goes_to_name()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "BETEILIGTE");
        var name = Named<TextBox>(window, "NewNameBox");
        FocusByTab(window, name);
        window.Type("Erika Testfrau");
        window.Tab();
        window.Type("0170 0000000");
        window.Tab();
        window.Type("Hauseigentümerin");
        window.Press(PhysicalKey.Enter);

        Assert.Equal("Erika Testfrau", Assert.Single(vm.InvolvedParties.Parties).Name);
        window.AssertFocused(name);
    }
}
