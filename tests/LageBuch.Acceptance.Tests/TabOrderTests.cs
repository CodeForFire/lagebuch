using Avalonia.Controls;
using Avalonia.Headless;
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

// #262 (UX review, "No accessibility support"): no explicit TabIndex exists anywhere in the app,
// so keyboard-only navigation relies entirely on Avalonia's default declaration-order tab
// navigation. Rather than sprinkling TabIndex speculatively, these tests verify the two riskiest
// layouts (many same-row fields/buttons) already tab in reading order -- if one of these starts
// failing, that specific spot needs an explicit TabIndex fix, not a blanket sweep.
public class TabOrderTests
{
    private static void Tab(Window window) => window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

    [AvaloniaFact]
    public void Forces_input_dock_tabs_left_to_right_in_reading_order()
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
        var vm = new ForcesViewModel(session, new FixedClock(), md, () => { });

        // Fill the dock so it sits in its ordinary state for the Tab sequence below. HINZUFÜGEN is
        // no longer disabled by missing input at all (#412) -- an empty field is answered on the
        // press instead -- so this is arrangement, not a precondition for reaching the button.
        vm.NewBrigade = "FFB Wache 1";
        vm.NewCallSign = "FFB 1/40/1";
        vm.NewMannschaftCount = 6;

        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // ClearVehicleButton is skipped: it stays IsVisible=false (no tab stop) until a vehicle
        // is selected, so it never appears in the reading order for a fresh, manual-entry dock.
        view.GetControl<ComboBox>("VehicleBox").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<ComboBox>("VehicleBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("BrigadeBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("CallSignBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("ZugfuehrerBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("OfficerBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("MannschaftBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("ScbaBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<ComboBox>("StatusBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<TextBox>("NotesBox").IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<Button>("AddForceButton").IsKeyboardFocusWithin);
    }

    private sealed class SampleProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Links = new[] { new Link("Wetterdienst", "https://dwd.de") },
        };

        public void Save(MasterDataSet set)
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

    [AvaloniaFact]
    public void Master_data_links_row_tabs_left_to_right_in_reading_order()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Links");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nameBox = view.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.PlaceholderText == AnonymizedExampleData.LinkNamePlaceholder);
        var urlBox = view.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.PlaceholderText == AnonymizedExampleData.LinkUrlPlaceholder);
        var upButton = view.GetVisualDescendants().OfType<Button>()
            .Single(b => (ToolTip.GetTip(b) as string) == "Nach oben");
        var downButton = view.GetVisualDescendants().OfType<Button>()
            .Single(b => (ToolTip.GetTip(b) as string) == "Nach unten");
        var removeButton = view.GetVisualDescendants().OfType<Button>()
            .Single(b => (ToolTip.GetTip(b) as string) == "Entfernen");

        nameBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(nameBox.IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(urlBox.IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(upButton.IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(downButton.IsKeyboardFocusWithin);

        Tab(window);
        Dispatcher.UIThread.RunJobs();
        Assert.True(removeButton.IsKeyboardFocusWithin);
    }
}
