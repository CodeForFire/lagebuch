using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Shared.Views;

namespace Feuerwehr.Acceptance.Tests;

// The shell uses a single 24px content gutter. Every primary band — the header bars,
// the module nav rail, and the footer actions — must share the same left edge so nothing
// juts out. These tests pin that contract so the rail can't drift back to a flush x=0.
public class LayoutAlignmentTests
{
    private const double Gutter = 24.0;

    private static double LeftInWindow(Visual v, Window window) =>
        v.TranslatePoint(new Point(0, 0), window)!.Value.X;

    private static double LeftInWindowY(Visual v, Window window) =>
        v.TranslatePoint(new Point(0, 0), window)!.Value.Y;

    // The TabControl's own tab-strip presenter (there are other ItemsPresenters in the tree).
    private static ItemsPresenter TabStrip(Window window) =>
        window.GetVisualDescendants().OfType<TabControl>().First()
            .GetVisualDescendants().OfType<ItemsPresenter>()
            .First(p => p.Name == "PART_ItemsPresenter");

    private static Window ShowWorkspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Nav_rail_starts_at_the_content_gutter_not_flush_to_the_edge()
    {
        var window = ShowWorkspace();

        Assert.Equal(Gutter, LeftInWindow(TabStrip(window), window), precision: 0);
    }

    // The status header promotes the Einsatznummer to the hero title (large MonoFont) with the
    // status readout sharing its line on the right. They live in the same grid row, so their
    // vertical centres must coincide — otherwise "● Offen" floats above or below the number instead
    // of reading as one status line. (This replaces the old boxed-chip height check: the chips are
    // gone; the invariant that survives is that the number and its status sit on one line.)
    [AvaloniaFact]
    public void Status_readout_sits_on_the_same_line_as_the_einsatznummer_hero()
    {
        var window = ShowWorkspace();

        var number = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "EinsatznummerValue");
        var status = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "StatusReadout");

        double CenterY(Visual v) => v.TranslatePoint(new Point(0, v.Bounds.Height / 2), window)!.Value.Y;
        var delta = Math.Abs(CenterY(number) - CenterY(status));

        // A few px of slack: the number is deliberately set in MonoFont against the status word's
        // DisplayFont, and the two have slightly different line-height metrics at the same pixel
        // size -- that residual gap is a font trait, not a layout bug, and varies a little across
        // platforms (this app's CI matrix includes windows-latest).
        Assert.True(delta <= 6,
            $"Einsatznummer hero and status readout centres are {delta:F0}px apart — they don't sit on one line.");
    }

    [AvaloniaFact]
    public void Header_bars_rail_and_footer_share_one_left_edge()
    {
        var window = ShowWorkspace();

        double Left(string name) => LeftInWindow(
            window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name), window);

        var reminderBarLeft = Left("ReminderBar");
        var tabStripLeft = LeftInWindow(TabStrip(window), window);
        var footerCloseLeft = Left("CloseButton");

        Assert.Equal(Gutter, reminderBarLeft, precision: 0);
        Assert.Equal(Gutter, tabStripLeft, precision: 0);
        Assert.Equal(Gutter, footerCloseLeft, precision: 0);
    }

    // MANNSCHAFT is the only star-sized column in the Atemschutz grid, so it absorbs the
    // content-driven growth of the eight auto-sized columns plus the fixed 300px action column.
    // Once their natural widths exceed the viewport it was squeezed to Avalonia's 20px
    // MinColumnWidth floor and the crew names disappeared entirely -- on a monitoring screen
    // where who is under air is the most safety-critical column on the row.
    [AvaloniaTheory]
    [InlineData(1613)]
    [InlineData(1400)]
    [InlineData(1280)]
    [InlineData(1100)]
    [InlineData(900)]
    public void Atemschutz_crew_column_stays_readable_at_any_window_width(double width)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var view = new ScbaView { DataContext = vm.Scba };
        var window = new Window { Content = view, Width = width, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        var crew = grid.Columns.Single(c => (string?)c.Header == "MANNSCHAFT");

        // Wide enough for two family names side by side; below this the grid must scroll
        // horizontally instead of silently hiding the column.
        Assert.True(crew.ActualWidth >= 160,
            $"MANNSCHAFT collapsed to {crew.ActualWidth:F0}px at a window width of {width}px.");
    }

    // Roster names are "Lastname, Firstname", so two of them joined by " / " overflow the crew
    // cell and a CSA crew of three has no chance. Each member gets its own line.
    [AvaloniaFact]
    public void Atemschutz_crew_is_rendered_one_member_per_line()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        vm.Scba.NewDesignation = "CSA-Trupp";
        vm.Scba.NewTruppfuehrer = "Hintersberger, Hans";
        vm.Scba.NewTruppmann = "Kreutzkamp, Bastian";
        vm.Scba.NewZweiterTruppmann = "Schormaier, Florian";
        vm.Scba.AddTruppCommand.Execute(null);

        var view = new ScbaView { DataContext = vm.Scba };
        var window = new Window { Content = view, Width = 1280, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rendered = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(b => b.Text).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        // Each name is its own text run, not one joined string that the cell then clips.
        foreach (var name in new[] { "Hintersberger, Hans", "Kreutzkamp, Bastian", "Schormaier, Florian" })
            Assert.Contains(name, rendered);
        Assert.DoesNotContain(rendered, s => s!.Contains("Hintersberger, Hans / "));

        // Existing in the visual tree is not the same as being visible. DataGrid rows are styled
        // to a fixed 40px, which laid the third name out at y=40 in a 41px row — present, and
        // entirely clipped. DesiredSize is useless here because the fixed height clamps it, so
        // the assertion has to be geometric: every name must actually fall inside its row.
        var crewCell = window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(c => c.ItemsSource is IReadOnlyList<string> l && l.Contains("Schormaier, Florian"));
        var row = crewCell.GetVisualAncestors().OfType<DataGridRow>().Single();

        foreach (var line in crewCell.GetVisualDescendants().OfType<TextBlock>())
        {
            var top = line.TranslatePoint(new Point(0, 0), row)!.Value.Y;
            Assert.True(top + line.Bounds.Height <= row.Bounds.Height,
                $"'{line.Text}' extends to {top + line.Bounds.Height:F0}px in a {row.Bounds.Height:F0}px row — clipped.");
        }
    }

    // The grid used to need ~1460px for ten columns, so on a normal window the action column was
    // cut off and the whole grid scrolled sideways. Seven stacked columns fit; this pins that.
    [AvaloniaTheory]
    [InlineData(1280)]
    [InlineData(1400)]
    [InlineData(1613)]
    public void Atemschutz_grid_fits_without_scrolling_sideways(double width)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        vm.Scba.NewDesignation = "CSA-Trupp";
        vm.Scba.NewTruppfuehrer = "Hintersberger, Hans";
        vm.Scba.NewTruppmann = "Kreutzkamp, Bastian";
        vm.Scba.NewZweiterTruppmann = "Schormaier, Florian";
        vm.Scba.NewCallSign = "FFB 1/41/1";
        vm.Scba.AddTruppCommand.Execute(null);

        var view = new ScbaView { DataContext = vm.Scba };
        var window = new Window { Content = view, Width = width, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        var total = grid.Columns.Sum(c => c.ActualWidth);

        Assert.True(total <= grid.Bounds.Width + 1,
            $"columns need {total:F0}px in a {grid.Bounds.Width:F0}px grid — the row overflows.");
    }
}
