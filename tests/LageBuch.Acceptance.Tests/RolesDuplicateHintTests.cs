using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #470: a duplicate the app cannot refuse -- an older Einsatzdatei, an imported .fwincident, or a
// joined device running other Stammdaten -- is neither blocked nor hidden. The hint under the add
// form names the holder, and the warning glyph marks the row that has to be handed over.
public class RolesDuplicateHintTests
{
    private const string Holder = "Müller";

    private const string Section = "Abschnitt Nord";

    [AvaloniaFact]
    public void A_held_funktion_names_its_holder_under_the_form()
    {
        var (_, view, vm) = ShowRoles(NewSession(), RoleUniqueness.UniquePerIncident);
        vm.Roles.NewRole = "EL";
        vm.Roles.NewPersonName = Holder;
        vm.Roles.NewSection = Section;
        vm.Roles.AddRoleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var hint = view.GetControl<TextBlock>("RoleConflictHint");
        Assert.False(hint.IsVisible); // nothing typed yet, so there is nothing to name

        // One holder is not a duplicate, so its row carries no marker -- the glyph's binding is
        // pinned in both directions here rather than only in its marked state.
        var clean = Assert.Single(vm.Roles.Roles);
        Assert.False(DuplicateGlyph(AktionCell(view, clean)).IsVisible);

        // Typed into the real box, not set on the view model, so what is exercised is the binding
        // the Lagebuchführer drives.
        view.GetControl<AutoCompleteBox>("RoleBox").Text = "EL";
        Dispatcher.UIThread.RunJobs();

        Assert.True(hint.IsVisible);
        Assert.Contains(Holder, hint.Text, StringComparison.Ordinal);
        Assert.Contains("EL", hint.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_duplicate_in_the_incident_shows_the_warning_glyph_and_its_handover_is_live()
    {
        // Seeded through the session, which is how an older file or another device delivers one:
        // the add form's refusal is the view model's, and the incident is the record of what
        // happened. Both rows, because either could be the one to hand over.
        var session = NewSession();
        session.AssignRole("EL", Holder, callSign: "FFB 12/1", section: Section);
        session.AssignRole("EL", "Schmidt", callSign: "Florian 2", section: Section);
        var (window, view, vm) = ShowRoles(session, RoleUniqueness.UniquePerIncident);

        Assert.Equal(2, vm.Roles.Roles.Count);
        foreach (var row in vm.Roles.Roles)
        {
            var cell = AktionCell(view, row);
            var glyph = DuplicateGlyph(cell);
            Assert.True(glyph.IsVisible);

            // A text-less control is invisible to any assistive layer without a name, and the
            // tooltip is what a sighted mouse user gets instead. Both say the same thing (#262).
            Assert.Equal("Funktion doppelt vergeben", AutomationProperties.GetName(glyph));
            Assert.Equal("Doppelt vergeben – Funktion übertragen", ToolTip.GetTip(glyph) as string);

            // The marker sits beside the button that fixes it, so a marker next to a dead button
            // would be a worse outcome than no marker at all.
            var handover = cell.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TransferRowButton");
            Assert.Equal("ÜBERTRAGEN", handover.Content);
            Assert.True(handover.IsEnabled);
        }

        // The add form's half of the same state, so the frame carries both halves of #470: what the
        // grid marks and what the form says. Typed into the box rather than set on the view model,
        // the same route the test above drives.
        view.GetControl<AutoCompleteBox>("RoleBox").Text = "EL";
        view.GetControl<TextBox>("SectionBox").Text = Section;
        Dispatcher.UIThread.RunJobs();

        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Join(dir, "rollen-after.png"));
        }
    }

    // The AKTION cell of a row. Its x:Names live in the column's DataTemplate, so GetControl cannot
    // see them -- the route ScbaTabRenderTests takes for a templated control.
    private static Control AktionCell(RolesView view, RoleAssignmentRow row) =>
        view.GetControl<DataGrid>("RolesGrid")
            .Columns.Single(c => (string?)c.Header == "AKTION")
            .GetCellContent(row)!;

    private static PathIcon DuplicateGlyph(Control cell) =>
        Assert.Single(cell.GetVisualDescendants().OfType<PathIcon>(), p => p.Name == "RoleDuplicateIcon");

    private static LocalIncidentSession NewSession() => TestSession.StartNew(
        new FakeStore(),
        new FixedClock(),
        new SessionOperator(Holder, "FFB 12/1"),
        "/x.fwincident",
        Array.Empty<(string, bool)>(),
        Array.Empty<(string, bool)>());

    private static (Window Window, RolesView View, IncidentWorkspaceViewModel Vm) ShowRoles(
        LocalIncidentSession session, RoleUniqueness uniqueness)
    {
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData() with { Roles = new[] { new Role("EL", uniqueness) } },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var view = new RolesView { DataContext = vm.Roles };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }
}
