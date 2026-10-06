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

// #543: in the Stammdaten editor "+ HINZUFÜGEN" puts the caret in the row it adds, and Entfernen
// asks first, with focus on ABBRECHEN, as every other remove in the app does. Both by keyboard.
public class StammdatenKeyboardTests
{
    private sealed class Provider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Roles = new[] { new Role("EL"), new Role("ZF") },
            UnitStatus = new[] { "Alarmiert" },
            TruppTypes = new[] { new TruppType("Angriffstrupp", 2, 30) },
            Links = new[] { new Link("Wetter", "https://example.org", string.Empty) },
            Personnel = new[] { new Person("Mustermann", "Max", null, null, null) },
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/44/1", 6) },
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

    private static (Window Window, MasterDataEditorViewModel Vm) ShowSection(string title)
    {
        var vm = new MasterDataEditorViewModel(new Provider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.First(s => s.Title == title);
        var window = new Window { Content = new MasterDataEditorView { DataContext = vm }, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    // Reached and pressed as a keyboard user does: focus, then Space.
    private static void Press(Window window, Button button)
    {
        button.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.Space);
    }

    private static Button Visible(Window window, Func<Button, bool> match) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && match(b));

    [AvaloniaTheory]
    [InlineData("Rollen")]
    [InlineData("Einheiten-Status")]
    [InlineData("Trupp-Typen")]
    [InlineData("Links")]
    [InlineData("Personal")]
    [InlineData("Fahrzeuge")]
    public void Hinzufuegen_puts_the_caret_in_the_new_rows_first_field(string title)
    {
        var (window, vm) = ShowSection(title);
        var section = vm.SelectedSection;
        object? added = null;
        Assert.NotNull(section);
        section.RowAdded += (_, e) => added = e.Row;

        Press(window, Visible(window, b => (b.Content as string) == "+ HINZUFÜGEN"));
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(added);
        var focused = window.FocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.Same(added, focused.GetSelfAndVisualAncestors().OfType<Control>().First(c => c.DataContext is not null).DataContext);
        Assert.True(
            focused.GetSelfAndVisualAncestors().Any(v => v is TextBox or AutoCompleteBox),
            $"focus went to {window.DescribeFocus()}, not into a field");
    }

    [AvaloniaFact]
    public void Entfernen_asks_first_with_focus_on_abbrechen()
    {
        var (window, vm) = ShowSection("Rollen");
        var roles = vm.Sections.OfType<RolesSection>().Single();
        var remove = window.GetVisualDescendants().OfType<Button>()
            .First(b => (ToolTip.GetTip(b) as string) == "Entfernen" && ReferenceEquals(b.DataContext, roles.Rows[0]));

        Press(window, remove);

        Assert.IsType<ConfirmDialogViewModel>(vm.PendingConfirm);
        Assert.Equal(2, roles.Rows.Count);
        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        window.AssertFocused(cancel);

        window.Press(PhysicalKey.Enter); // a reflex Enter cancels (#538)

        Assert.Null(vm.PendingConfirm);
        Assert.Equal(2, roles.Rows.Count);
    }
}
