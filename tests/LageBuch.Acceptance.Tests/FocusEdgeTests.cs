using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #541: keyboard focus is visible on every interactive control, in its own FocusColor token rather
// than the emergency red. Checked on the rendered pixels, because a style that never draws (as
// overriding Fluent's focus margin does) passes every check on properties.
public class FocusEdgeTests
{
    private static Color FocusColor()
    {
        Assert.True(Application.Current!.TryFindResource("FocusColor", out var value));
        return Assert.IsType<Color>(value);
    }

    // Pixels of the focus colour in and just around the control; anti-aliasing allows some drift.
    private static int FocusPixels(Window window, Control target)
    {
        var focus = FocusColor();

        // The headless Skia frame is RGBA: red in the lowest byte.
        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = new FramePixels(frame);
        var area = new Rect(target.TranslatePoint(default, window)!.Value, target.Bounds.Size).Inflate(3);
        var count = 0;
        for (var y = Math.Max(0, (int)area.Top); y < Math.Min(pixels.Height, (int)area.Bottom); y++)
        {
            for (var x = Math.Max(0, (int)area.Left); x < Math.Min(pixels.Width, (int)area.Right); x++)
            {
                var raw = pixels.Raw(x, y);
                if (Math.Abs((raw & 0xFF) - focus.R) <= 40
                    && Math.Abs(((raw >> 8) & 0xFF) - focus.G) <= 40
                    && Math.Abs(((raw >> 16) & 0xFF) - focus.B) <= 40)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // A drawn edge covers most of the control's perimeter; text or an icon in a similar hue would not.
    private static void AssertEdge(Window window, Control target)
    {
        var perimeter = (int)(target.Bounds.Width + target.Bounds.Height);
        var found = FocusPixels(window, target);
        Assert.True(found >= perimeter, $"{KeyboardInput.Describe(target)} shows {found} focus-colour pixels, expected at least {perimeter} for an edge.");
    }

    private static (Window Window, ForcesView View) ShowForces()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var md = MasterDataSet.Empty with
        {
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
            UnitStatus = new[] { "Alarmiert" },
        };
        var view = new ForcesView { DataContext = new ForcesViewModel(session, new FixedClock(), md, () => { }) };
        var window = new Window { Content = view, Width = 1400, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaFact]
    public void Every_control_in_the_Kraefte_dock_shows_the_focus_edge_when_tabbed_to()
    {
        var (window, view) = ShowForces();
        Control[] chain =
        [
            view.GetControl<ComboBox>("VehicleBox"),
            view.GetControl<TextBox>("BrigadeBox"),
            view.GetControl<TextBox>("CallSignBox"),
            view.GetControl<TextBox>("ZugfuehrerBox"),
            view.GetControl<TextBox>("OfficerBox"),
            view.GetControl<TextBox>("MannschaftBox"),
            view.GetControl<TextBox>("ScbaBox"),
            view.GetControl<ComboBox>("StatusBox"),
            view.GetControl<TextBox>("NotesBox"),
            view.GetControl<Button>("AddForceButton"),
        ];
        chain[0].Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        foreach (var control in chain)
        {
            window.AssertFocused(control);
            AssertEdge(window, control);
            window.Tab();
        }
    }

    // :focus-visible, not :focus: a click, or a touch on Android, must not light up a button.
    [AvaloniaFact]
    public void A_button_focused_by_the_pointer_shows_no_focus_edge()
    {
        var (window, view) = ShowForces();
        var button = view.GetControl<Button>("AddForceButton");

        button.Focus(NavigationMethod.Pointer);
        Dispatcher.UIThread.RunJobs();

        window.AssertFocused(button);
        Assert.Equal(0, FocusPixels(window, button));
    }

    // The custom-templated rail TabItem and ListBoxItem, and DataGrid's own per-cell ring, all draw
    // in the focus colour rather than Fluent's white.
    [AvaloniaFact]
    public void Rail_tab_list_item_and_grid_cell_show_the_focus_edge_in_the_focus_colour()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var railItem = (TabItem)WorkspaceRenderHelper.Tabs(window).ContainerFromIndex(2)!;
        Assert.False(railItem.IsSelected);
        railItem.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        AssertEdge(window, railItem);

        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        vm.Forces.SelectedVehicle = vm.Forces.VehicleOptions[0];
        vm.Forces.AddForceCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "ForcesGrid");
        grid.SelectedIndex = 0;
        grid.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        var cell = grid.GetVisualDescendants().OfType<DataGridCell>().First(c => c.IsEffectivelyVisible);
        AssertEdge(window, cell);

        var editor = new MasterDataEditorViewModel(new StaticMasterData(WorkspaceRenderHelper.MasterData()), new FakeDialogs(), new NoFiles());
        var editorWindow = new Window { Content = new MasterDataEditorView { DataContext = editor }, Width = 1080, Height = 680 };
        editorWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var listItem = (ListBoxItem)editorWindow.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "CategoryList").ContainerFromIndex(1)!;
        Assert.False(listItem.IsSelected);
        listItem.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        AssertEdge(editorWindow, listItem);
    }

    private sealed class StaticMasterData(MasterDataSet set) : IMasterDataProvider
    {
        public MasterDataSet Get() => set;

        public void Save(MasterDataSet s)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }
}
