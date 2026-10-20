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

// A brigade's real roster is far longer than the screen: 39 Fahrzeuge, 38 people. Built all at
// once, opening the category took most of a second, so the long lists build only the rows on
// screen. That must not cost the keyboard anything: + HINZUFÜGEN still lands in the new row at
// the bottom (#543), and Tab and Shift+Tab still walk every row, including those not built yet.
public class StammdatenLongListTests
{
    private const int RowCount = 16;

    private sealed class LongProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Personnel = Enumerable.Range(1, RowCount).Select(i => new Person($"Muster{i}", "Max", null, null, null)).ToArray(),
            Vehicles = Enumerable.Range(1, RowCount).Select(i => new Vehicle("FFB Wache 1", $"FFB 1/{i}/1", 6)).ToArray(),
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

    private static (Window Window, MasterDataEditorViewModel Vm, List<object> Rows) ShowSection(string title)
    {
        var vm = new MasterDataEditorViewModel(new LongProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.First(s => s.Title == title);
        var window = new Window { Content = new MasterDataEditorView { DataContext = vm }, Width = 1080, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var rows = vm.SelectedSection switch
        {
            PersonnelSection p => p.Rows.ToList<object>(),
            VehiclesSection v => v.Rows.ToList<object>(),
            _ => throw new InvalidOperationException(title),
        };
        return (window, vm, rows);
    }

    // The section's own row list, not a suggestion box's popup list inside a row.
    private static ItemsControl RowList(Window window) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(l => l.DataContext is PersonnelSection or VehiclesSection && l.ItemCount == RowCount);

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
        for (var i = 0; i < RowCount * 20 && reached.Count < rows.Count; i++)
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
    public void A_long_list_builds_only_the_rows_on_screen(string title)
    {
        var (window, _, rows) = ShowSection(title);

        var built = Enumerable.Range(0, rows.Count).Count(i => RowList(window).ContainerFromIndex(i) is not null);

        Assert.InRange(built, 1, rows.Count / 2);
    }

    [AvaloniaTheory]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
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
    public void Tab_walks_every_row_of_a_long_list(string title)
    {
        var (window, _, rows) = ShowSection(title);
        FirstField(window, rows[0]).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        AssertWalksEveryRow(window, rows, RawInputModifiers.None);
    }

    [AvaloniaTheory]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
    public void Shift_tab_walks_back_up_every_row_of_a_long_list(string title)
    {
        var (window, _, rows) = ShowSection(title);
        RowList(window).ScrollIntoView(rows.Count - 1);
        Dispatcher.UIThread.RunJobs();
        FirstField(window, rows[^1]).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        AssertWalksEveryRow(window, rows, RawInputModifiers.Shift);
    }
}
