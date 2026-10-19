using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #605: the Kräfte view showed ZF, GF and AGT bare everywhere, spelled GF out as
// "Führungskräfte" where a Stärkemeldung means Gruppenführer, and gave the 1/1/6/8 notation no
// legend at all. These pin the explanations where an Einsatzkraft hovers or a screen reader reads.
public class ForcesWordingTests
{
    private const string Legend = "Zugführer / Gruppenführer / Mannschaft / Gesamt";

    private static (Window Window, ForcesView View, ForcesViewModel Vm) ShowForces()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddForceUnit("FFB Wache 1", 6, null, "Alarmiert", null);
        var md = MasterDataSet.Empty with
        {
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
            UnitStatus = new[] { "Alarmiert" },
        };
        var vm = new ForcesViewModel(session, new FixedClock(), md, () => { });
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static Button RowButton(ForcesView view, Func<Button, bool> match) =>
        view.GetControl<DataGrid>("ForcesGrid").GetVisualDescendants().OfType<Button>().Single(match);

    private static void Open(Window window, Button button)
    {
        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(1200, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 1200, 600));
        Dispatcher.UIThread.RunJobs();
    }

    // What a hover over the visual shows: its own tip, or the nearest ancestor's, which is how a
    // field header drawn by the HeaderedContentControl's template gets the field's tooltip.
    private static string? TipOver(Visual visual) =>
        visual.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => ToolTip.GetTip(c) as string)
            .FirstOrDefault(tip => tip is not null);

    private static HeaderedContentControl Field(Visual root, string header) =>
        root.GetVisualDescendants().OfType<HeaderedContentControl>()
            .First(h => (h.Header as string) == header);

    [AvaloniaFact]
    public void Strength_flyout_spells_GF_out_as_Gruppenfuehrer()
    {
        var (window, view, _) = ShowForces();
        Open(window, RowButton(view, b => AutomationProperties.GetName(b) == "Stärke korrigieren"));

        var headers = window.GetVisualDescendants().OfType<HeaderedContentControl>()
            .Select(h => h.Header as string)
            .ToList();
        Assert.Contains("GRUPPENFÜHRER (GF)", headers);
        Assert.DoesNotContain("FÜHRUNGSKRÄFTE (GF)", headers);
    }

    [AvaloniaFact]
    public void Each_strength_box_in_the_dock_is_named_for_what_it_counts()
    {
        var (_, view, _) = ShowForces();

        foreach (var (box, name) in new[]
        {
            ("ZugfuehrerBox", "Zugführer"),
            ("OfficerBox", "Gruppenführer"),
            ("MannschaftBox", "Mannschaft"),
        })
        {
            var textBox = view.GetControl<TextBox>(box);
            Assert.Equal(name, AutomationProperties.GetName(textBox));
            Assert.Equal(name, ToolTip.GetTip(textBox) as string);
        }
    }

    [AvaloniaFact]
    public void Dock_headers_spell_out_their_abbreviations()
    {
        var (_, view, _) = ShowForces();

        Assert.Equal("Zugführer / Gruppenführer / Mannschaft", ToolTip.GetTip(Field(view, "STÄRKE ZF / GF / MANN")) as string);
        Assert.Equal("Atemschutzgeräteträger", ToolTip.GetTip(Field(view, "DAVON AGT")) as string);
    }

    [AvaloniaFact]
    public void Every_bare_AGT_label_spells_out_Atemschutzgeraetetraeger()
    {
        var (_, view, _) = ShowForces();

        // The header tile's DAVON AGT, the dock field's header, the grid column header, and the
        // phone card's AGT overline once the card layout is realised.
        var labels = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text is "DAVON AGT" or "AGT")
            .ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => Assert.Equal("Atemschutzgeräteträger", TipOver(t)));

        var column = view.GetControl<DataGrid>("ForcesGrid").Columns.Single(c => (c.Header as string) == "AGT");
        Assert.NotNull(column.HeaderTemplate);
    }

    [AvaloniaFact]
    public void Strength_readouts_carry_the_notation_legend()
    {
        var (_, view, _) = ShowForces();

        Assert.Equal(Legend, ToolTip.GetTip(view.GetControl<Control>("TotalStrengthReadout")) as string);

        // Every readout of a row's Stärke, grid cell and phone card alike, is the prefix text
        // and the Gesamt side by side in one panel, and that panel explains the notation.
        var readouts = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "0/0/6/")
            .Select(t => t.GetVisualParent())
            .OfType<Control>()
            .ToList();
        Assert.NotEmpty(readouts);
        Assert.All(readouts, panel => Assert.Equal(Legend, ToolTip.GetTip(panel) as string));
    }

    [AvaloniaFact]
    public void Verlauf_flyout_opens_with_the_notation_legend()
    {
        var (window, view, vm) = ShowForces();
        var row = Assert.Single(vm.Forces);
        row.ZugfuehrerCount = 1;
        row.ApplyStrengthEditCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Open(window, RowButton(view, b => (b.Content as string) == "Verlauf"));

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains($"Stärke: {Legend}", texts);
    }
}
