using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// The WOHNUNG BEARBEITEN sidebar had no ScrollViewer at all: a DockPanel.Dock="Right" Border
// around a StackPanel, which measures against infinity, so anything that did not fit was simply
// clipped away. ABBRECHEN and FERTIG are last in that stack and were therefore the first thing
// lost -- and since #242 FERTIG is the SINGLE commit point of the whole sidebar, losing it means
// the Wohnung cannot be edited at all. A crew measures 10 ppm and cannot save it.
//
// These render deliberately short, which is what the other CO test files do not do
// (1200x700 and 1920x1032) and why this went unnoticed for so long. 600 is the app's own
// MinWidth/MinHeight floor (MainWindow.axaml), so it is a size a user can actually produce;
// Android, where MinHeight does not apply at all, is worse still.
public class CoMessprotokollEditorReachabilityTests
{
    private const double ShortHeight = 600.0;

    private static (Window Window, CoMessprotokollView View, CoMessprotokollViewModel Vm, LocalIncidentSession Session) ShowEditor(
        int readings, double height = ShortHeight)
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Huber", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 1, 1);
        var vm = new CoMessprotokollViewModel(session, clock, () => { });
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var haus = session.Incident.Buildings[0].Id;
        for (var i = 0; i < readings; i++)
        {
            clock.Now = clock.Now.AddMinutes(3);
            session.RecordCoValue(haus, 0, 1, 10 + i);
        }

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, session);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    // Vertical sibling of CommandBarReachabilityTests.HorizontalBounds.
    private static (double Top, double Bottom) VerticalBounds(Visual v, Visual relativeTo)
    {
        var top = v.TranslatePoint(new Point(0, 0), relativeTo)!.Value.Y;
        return (top, top + v.Bounds.Height);
    }

    private static void AssertConfirmIsInsideTheSidebar(CoMessprotokollView view, int readings)
    {
        var sidebar = view.GetControl<Border>("DwellingEditor");
        var confirm = view.GetControl<Button>("EditorConfirmButton");
        var (top, bottom) = VerticalBounds(confirm, sidebar);

        var message = $"FERTIG spans y=[{top:0}..{bottom:0}] inside a {sidebar.Bounds.Height:0}px sidebar " +
            $"({readings} reading(s)) — it is clipped off the bottom by {bottom - sidebar.Bounds.Height:0}px " +
            "and cannot be clicked, so the edit can never be committed.";
        Assert.True(bottom <= sidebar.Bounds.Height, message);
    }

    // 0 readings hides the Messreihe entirely, so that case pins the part of this bug that has
    // nothing to do with #424: the sidebar was already taller than a 600px window before the
    // series existed. 25 is the #424 aggravation -- every committed reading pushed the next
    // FERTIG further down, so the button that writes a reading was buried by the readings it
    // had written.
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(25)]
    public void FERTIG_stays_reachable_on_a_short_window(int readings)
    {
        var (window, view, _, _) = ShowEditor(readings);
        Capture(window, $"co-sidebar-kurz-{readings}.png");

        AssertConfirmIsInsideTheSidebar(view, readings);
    }

    [AvaloniaFact]
    public void The_sidebar_scrolls_instead_of_clipping()
    {
        var (_, view, _, _) = ShowEditor(readings: 25);

        var scroll = view.GetControl<ScrollViewer>("EditorScroll");
        var message = $"sidebar content is {scroll.Extent.Height:F0}px in a {scroll.Viewport.Height:F0}px " +
            "viewport — nothing to scroll, so the overflow is being clipped instead.";
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, message);
    }

    // A Wohnung measured all shift must not push BEZEICHNUNG and the Bewohner fields out of view.
    [AvaloniaFact]
    public void The_Messreihe_is_capped_and_scrolls_on_its_own()
    {
        var (_, view, _, _) = ShowEditor(readings: 25, height: 1032);

        var messreihe = view.GetControl<ScrollViewer>("MessreiheScroll");
        var overflow = $"Messreihe is {messreihe.Extent.Height:F0}px in a {messreihe.Viewport.Height:F0}px " +
            "viewport — 25 readings did not exceed the cap, so it is not actually capped.";
        Assert.True(messreihe.Extent.Height > messreihe.Viewport.Height, overflow);

        var capped = $"Messreihe rendered {messreihe.Bounds.Height:F0}px tall, " +
            $"above its {messreihe.MaxHeight:F0}px cap.";
        Assert.True(messreihe.Bounds.Height <= messreihe.MaxHeight + 1, capped);
    }

    // Sibling of CoMessprotokollMixedBuildingTests.Tiles_never_run_under_the_vertical_scrollbar:
    // AllowAutoHide="False" reserves layout width, so the bar must not sit on the input fields.
    [AvaloniaFact]
    public void The_CO_field_never_runs_under_the_sidebar_scrollbar()
    {
        var (_, view, _, _) = ShowEditor(readings: 25);

        var scroll = view.GetControl<ScrollViewer>("EditorScroll");

        // TemplatedParent, not just the first match: the Messreihe has its own nested scroller
        // inside this one, so a plain descendant search finds two vertical bars.
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical
                && ReferenceEquals(b.TemplatedParent, scroll));
        Assert.True(bar.IsEffectivelyVisible, "expected a visible vertical scrollbar with 25 readings.");

        var input = view.GetControl<NumericUpDown>("CoValueInput");
        var inputRight = input.TranslatePoint(new Point(input.Bounds.Width, 0), scroll)!.Value.X;
        var barLeft = bar.TranslatePoint(new Point(0, 0), scroll)!.Value.X;

        var message = $"CO-WERT ends at x={inputRight:0} but the scrollbar starts at x={barLeft:0} " +
            "— it sits on the field.";
        Assert.True(inputRight <= barLeft + 1, message);
    }
}
