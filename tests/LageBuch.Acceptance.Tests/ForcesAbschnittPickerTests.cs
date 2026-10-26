using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The KRÄFTE list places a unit in an Abschnitt through the shared NodeChoices picker: a column in
// the grid, a row on every phone card. Doubles as the PR screenshot capture.
public class ForcesAbschnittPickerTests
{
    private const string PickerName = "Abschnitt wählen";

    private static (Window Window, ForcesView View, ForcesViewModel Vm) Show(bool narrow)
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Mustermann", "FFB 12/1");
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var emmering = session.Incident.AddForceUnit(clock, op, "FF Emmering", 9, "Florian Emmering 40/1", "Im Einsatz");
        session.Incident.AddForceUnit(clock, op, "FF Aich", 7, "Florian Aich 42/1", "Auf Anfahrt");
        var abschnitt = session.Incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var nord = session.Incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Nord", abschnitt.Id);
        session.Incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz");
        session.MoveForceUnit(emmering.Id, nord.Id);

        var md = MasterDataSet.Empty with { UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Im Einsatz" } };
        var vm = new ForcesViewModel(session, clock, md, () => { });
        var view = new ForcesView { DataContext = vm };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = narrow ? 800 : 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static List<Button> Pickers(Visual root) =>
        root.GetVisualDescendants().OfType<Button>()
            .Where(b => AutomationProperties.GetName(b) == PickerName && b.IsEffectivelyVisible)
            .ToList();

    private static void Capture(Visual visual, string name)
    {
        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = TopLevel.GetTopLevel(visual)!.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_refused_Abschnitt_pick_says_so_above_the_list(bool narrow)
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Mustermann", "FFB 12/1");
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddForceUnit(clock, op, "FF Emmering", 9, "Florian Emmering 40/1", "Im Einsatz");
        var abschnitt = session.Incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var vm = new ForcesViewModel(session, clock, MasterDataSet.Empty, () => { });
        var view = new ForcesView { DataContext = vm };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = narrow ? 800 : 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var line = view.GetControl<TextBlock>("ForcesActionError");
        Assert.False(line.IsEffectivelyVisible);
        var pick = vm.Forces.Single().NodeChoices.Single(c => c.NodeId == abschnitt.Id);
        vm.Dispose(); // the list keeps offering the Abschnitt a peer dissolves next
        session.DissolveCommandNode(abschnitt.Id);

        pick.SelectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(line.IsEffectivelyVisible);
        Assert.StartsWith("Nicht möglich: ", line.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_grid_has_an_ABSCHNITT_column_showing_where_each_unit_sits()
    {
        var (window, view, _) = Show(narrow: false);

        var grid = view.GetControl<DataGrid>("ForcesGrid");
        Assert.Contains(grid.Columns, c => (c.Header as string) == "ABSCHNITT");
        var pickers = Pickers(grid);
        Assert.Equal(2, pickers.Count);
        Assert.Equal(
            new[] { "Abschnitt 1 / Nord", "—" },
            pickers.Select(p => ((TextBlock)p.Content!).Text!));
        Assert.All(pickers, p => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(p) as string)));
        Capture(window, "kraefte-abschnitt-after.png");
    }

    [AvaloniaFact]
    public void The_flyout_lists_the_choices_marks_the_current_one_and_places_the_unit()
    {
        var (_, view, vm) = Show(narrow: false);
        var picker = Pickers(view.GetControl<DataGrid>("ForcesGrid"))[1];

        picker.Flyout!.ShowAt(picker);
        Dispatcher.UIThread.RunJobs();

        var content = (Control)((Flyout)picker.Flyout).Content!;
        var entries = content.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(
            new[] { NodeChoices.UnplacedLabel, "Abschnitt 1", "Abschnitt 1 / Nord", "Bereitstellung Marktplatz" },
            entries.Select(b => b.GetVisualDescendants().OfType<TextBlock>().Last().Text!));
        var ticked = entries.Where(b => b.GetVisualDescendants().OfType<TextBlock>().First().IsEffectivelyVisible).ToList();
        Assert.Single(ticked);
        Assert.Contains(NodeChoices.UnplacedLabel, ticked[0].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Capture(content, "kraefte-abschnitt-flyout.png");

        var marktplatz = entries.Single(b => b.GetVisualDescendants().OfType<TextBlock>().Last().Text == "Bereitstellung Marktplatz");
        Assert.True(picker.Flyout.IsOpen);

        // A real press and release, in the popup's own top level.
        var popup = TopLevel.GetTopLevel(marktplatz)!;
        var centre = marktplatz.TranslatePoint(new Point(marktplatz.Bounds.Width / 2, marktplatz.Bounds.Height / 2), popup)!.Value;
        popup.MouseDown(centre, MouseButton.Left);
        popup.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // A Button inside a Flyout does not dismiss it, and the kept row never rebuilds the popup.
        Assert.False(picker.Flyout.IsOpen);

        Assert.Equal("Bereitstellung Marktplatz", vm.Forces[1].NodeLabel);
        Assert.Equal("Bereitstellung Marktplatz", ((TextBlock)picker.Content!).Text!);
    }

    [AvaloniaFact]
    public void Phone_cards_carry_the_same_picker()
    {
        var (window, view, _) = Show(narrow: true);

        var cards = view.GetControl<ItemsControl>("ForcesCards");
        var pickers = Pickers(cards);
        Assert.Equal(2, pickers.Count);
        Assert.Equal(
            new[] { "Abschnitt 1 / Nord", NodeChoices.UnplacedLabel },
            pickers.Select(p => ((TextBlock)p.Content!).Text!));
        Assert.Equal(2, cards.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text == "ABSCHNITT"));
        Capture(window, "kraefte-abschnitt-narrow.png");
    }
}
