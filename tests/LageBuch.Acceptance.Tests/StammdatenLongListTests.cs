using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// A brigade's real roster is far longer than the screen: 39 Fahrzeuge, 38 people, and some 320
// Straßen once a Gemeinde's streets come from OpenStreetMap (#461). Built all at
// once, opening the category took most of a second, so the long lists build only the rows on
// screen. That must not cost the keyboard anything: + HINZUFÜGEN still lands in the new row at
// the bottom (#543), and Tab and Shift+Tab still walk every row, including those not built yet.
public class StammdatenLongListTests
{
    private const int RowCount = 16;

    // The walks press Tab through every field of every row, and each press renders two headless
    // frames, so they get a short list in a short window, which still leaves its far end unbuilt.
    // Each walk asserts that before it starts, so a fixture that stops reaching below the fold fails
    // instead of passing without testing anything.
    private const int WalkRowCount = 6;
    private const int WalkHeight = 320;

    private sealed class LongProvider(int rowCount) : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Personnel = Enumerable.Range(1, rowCount).Select(i => new Person($"Muster{i}", "Max", null, null, null)).ToArray(),
            Vehicles = Enumerable.Range(1, rowCount).Select(i => new Vehicle("FFB Wache 1", $"FFB 1/{i}/1", 6)).ToArray(),
            Streets = Enumerable.Range(1, rowCount).Select(i => new Street($"Musterweg {i}", "Puch", "82256", "Fürstenfeldbruck")).ToArray(),
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

    private static (Window Window, MasterDataEditorViewModel Vm, List<object> Rows) ShowSection(
        string title, int rowCount = RowCount, int height = 480)
    {
        var vm = new MasterDataEditorViewModel(new LongProvider(rowCount), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.First(s => s.Title == title);
        var window = new Window { Content = new MasterDataEditorView { DataContext = vm }, Width = 1080, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var rows = vm.SelectedSection switch
        {
            PersonnelSection p => p.Rows.ToList<object>(),
            VehiclesSection v => v.Rows.ToList<object>(),
            StreetsSection s => s.Rows.ToList<object>(),
            _ => throw new InvalidOperationException(title),
        };
        return (window, vm, rows);
    }

    // The section's own row list, not a suggestion box's popup list inside a row.
    private static ItemsControl RowList(Window window, List<object> rows) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(l => l.DataContext is PersonnelSection or VehiclesSection or StreetsSection && l.ItemCount == rows.Count);

    private static Control FirstField(Window window, object row) =>
        window.GetVisualDescendants().OfType<Control>()
            .First(c => c is TextBox or AutoCompleteBox && c.IsEffectivelyVisible && ReferenceEquals(c.DataContext, row));

    private static object? FocusedRow(Window window, List<object> rows) =>
        (window.FocusedElement() as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => c.DataContext).FirstOrDefault(d => d is not null && rows.Contains(d));

    // Presses the key until every row has held focus, or focus leaves the list.
    private static void AssertWalksEveryRow(Window window, List<object> rows, RawInputModifiers modifiers)
    {
        var start = FocusedRow(window, rows);
        Assert.NotNull(start);
        var reached = new HashSet<object>(ReferenceEqualityComparer.Instance) { start };
        var last = window.DescribeFocus();
        for (var i = 0; i < rows.Count * 20 && reached.Count < rows.Count; i++)
        {
            last = window.DescribeFocus();
            window.Press(PhysicalKey.Tab, modifiers);
            if (FocusedRow(window, rows) is not { } row)
            {
                break;
            }

            reached.Add(row);
        }

        Assert.True(
            reached.Count == rows.Count,
            $"reached {reached.Count} of {rows.Count} rows, then went from {last} to {window.DescribeFocus()}");
    }

    [AvaloniaTheory]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
    [InlineData("Straßen")]
    public void A_long_list_builds_only_the_rows_on_screen(string title)
    {
        var (window, _, rows) = ShowSection(title);

        var built = Enumerable.Range(0, rows.Count).Count(i => RowList(window, rows).ContainerFromIndex(i) is not null);

        Assert.InRange(built, 1, rows.Count / 2);
    }

    [AvaloniaTheory]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
    [InlineData("Straßen")]
    public void Hinzufuegen_reaches_the_new_row_below_a_long_list(string title)
    {
        var (window, vm, _) = ShowSection(title);
        var section = vm.SelectedSection;
        object? added = null;
        Assert.NotNull(section);
        section.RowAdded += (_, e) => added = e.Row;

        var add = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.IsEffectivelyVisible && (b.Content as string) == "+ HINZUFÜGEN");
        add.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.Space);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(added);
        var focused = window.FocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.Same(added, focused.GetSelfAndVisualAncestors().OfType<Control>().First(c => c.DataContext is not null).DataContext);
        Assert.True(
            focused.GetSelfAndVisualAncestors().Any(v => v is TextBox or AutoCompleteBox),
            $"focus went to {window.DescribeFocus()}, not into a field");
    }

    // Entering a roster by keyboard: Tab from the first row must not fall out of the list at
    // the bottom of the screen.
    [AvaloniaTheory]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
    [InlineData("Straßen")]
    public void Tab_walks_every_row_of_a_long_list(string title)
    {
        var (window, _, rows) = ShowSection(title, WalkRowCount, WalkHeight);
        Assert.Null(RowList(window, rows).ContainerFromIndex(rows.Count - 1));
        FirstField(window, rows[0]).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        AssertWalksEveryRow(window, rows, RawInputModifiers.None);
    }

    [AvaloniaTheory]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
    [InlineData("Straßen")]
    public void Shift_tab_walks_back_up_every_row_of_a_long_list(string title)
    {
        var (window, _, rows) = ShowSection(title, WalkRowCount, WalkHeight);
        RowList(window, rows).ScrollIntoView(rows.Count - 1);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(RowList(window, rows).ContainerFromIndex(0));
        FirstField(window, rows[^1]).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        AssertWalksEveryRow(window, rows, RawInputModifiers.Shift);
    }
}
