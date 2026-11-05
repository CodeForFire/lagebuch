using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;
using LageBuch.TestFixtures;
using Microsoft.Data.Sqlite;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// Stammdaten Import and Export through the rendered editor, against a file that sets every field
/// of every category (<see cref="StammdatenFixture"/>). The JSON format, the store and the view
/// model are proved lossless one level down; what only shows here is a control that rewrites a
/// value merely by displaying it -- a NumericUpDown clamping to its Maximum, an AutoCompleteBox or
/// a ComboBox writing its own idea of the value back through a two-way binding.
/// </summary>
public sealed class MasterDataRoundTripTests : IDisposable
{
    // Tall enough that no row of the fixture is left unrealized by a virtualizing list: a row
    // that is never built never binds, and could not show a loss.
    private const double WindowHeight = 2400;

    private readonly string _dir = Path.Join(Path.GetTempPath(), $"md-ui-roundtrip-{Guid.NewGuid():N}");

    public MasterDataRoundTripTests() => Directory.CreateDirectory(_dir);

    private string StorePath => Path.Join(_dir, "masterdata.db");

    private string ExportPath => Path.Join(_dir, "export.json");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [AvaloniaTheory]
    [InlineData(1080.0)]
    [InlineData(412.0)] // Android's phone width, where Personal and Fahrzeuge use their stacked rows
    public async Task Import_save_and_export_through_the_rendered_editor_lose_nothing(double width)
    {
        var dialogs = new FakeDialogs { ImportJsonPath = StammdatenFixture.Path, ExportJsonPath = ExportPath };

        var (importer, importWindow) = Show(new MasterDataProvider(StorePath), dialogs, width);
        await importer.ImportCommand.ExecuteAsync(null);
        Assert.Null(importer.FileError);
        VisitEverySection(importer);
        importer.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        importWindow.Close();

        var (exporter, exportWindow) = Show(new MasterDataProvider(StorePath), dialogs, width);
        VisitEverySection(exporter);
        await exporter.ExportCommand.ExecuteAsync(null);
        Assert.Null(exporter.FileError);
        exportWindow.Close();

        Assert.Equal(StammdatenFixture.Json, StammdatenFixture.Canonical(await File.ReadAllTextAsync(ExportPath)));
    }

    [AvaloniaTheory]
    [InlineData(1080.0)]
    [InlineData(412.0)]
    public async Task Opening_every_category_of_stored_stammdaten_changes_nothing(double width)
    {
        // The dirty flag is the sharper witness: a control that writes a value back marks the
        // editor changed, so leaving it would ask to discard edits nobody made.
        using (var fixture = File.OpenRead(StammdatenFixture.Path))
        {
            MasterDataStore.Save(StorePath, MasterDataJson.Parse(fixture));
        }

        var dialogs = new FakeDialogs { ExportJsonPath = ExportPath };

        var (vm, window) = Show(new MasterDataProvider(StorePath), dialogs, width);
        VisitEverySection(vm);

        Assert.False(vm.IsDirty, "only looking at the Stammdaten must not change them");
        await vm.ExportCommand.ExecuteAsync(null);
        window.Close();
        Assert.Equal(StammdatenFixture.Json, StammdatenFixture.Canonical(await File.ReadAllTextAsync(ExportPath)));
    }

    // A hand-edited file may carry numbers the editor's spinners cannot show. The import takes
    // them as they are, so the editor must not quietly replace them with its own limits.
    [AvaloniaFact]
    public async Task Numbers_beyond_what_the_editor_offers_are_not_rewritten_by_showing_them()
    {
        MasterDataSet fixture;
        using (var stream = File.OpenRead(StammdatenFixture.Path))
        {
            fixture = MasterDataJson.Parse(stream);
        }

        var outOfRange = fixture with
        {
            Settings = fixture.Settings with
            {
                IlsReminderIntervalMinutes = 180,
                IlsReminderFollowUpIntervalMinutes = 150,
                ReturnPressureBar = 450,
            },
            TruppTypes = [.. fixture.TruppTypes.Select(t => t with { MaxDurationMinutes = 300 })],
            Vehicles = [.. fixture.Vehicles.Select(v => v with { Seats = 120 })],
        };
        MasterDataStore.Save(StorePath, outOfRange);
        var dialogs = new FakeDialogs { ExportJsonPath = ExportPath };

        var (vm, window) = Show(new MasterDataProvider(StorePath), dialogs, 1080);
        VisitEverySection(vm);

        // The spinner really is on screen and really holds the value above its Maximum of 120:
        // without this the test would pass just as well over a template that never got built.
        vm.SelectedSection = vm.Sections.OfType<SettingsSection>().Single();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(
            window.GetVisualDescendants().OfType<NumericUpDown>(),
            n => n.IsEffectivelyVisible && n.Value == 180m && n.Maximum < 180m);

        Assert.False(vm.IsDirty, "only looking at the Stammdaten must not change them");
        await vm.ExportCommand.ExecuteAsync(null);
        window.Close();
        Assert.Equal(
            StammdatenFixture.Canonical(MasterDataJson.Serialize(outOfRange)),
            StammdatenFixture.Canonical(await File.ReadAllTextAsync(ExportPath)));
    }

    private static (MasterDataEditorViewModel Vm, Window Window) Show(IMasterDataProvider provider, FakeDialogs dialogs, double width)
    {
        var vm = new MasterDataEditorViewModel(provider, dialogs, new MasterDataFileService());
        var window = new Window { Content = new MasterDataEditorView { DataContext = vm }, Width = width, Height = WindowHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Otherwise the phone case would silently test the desktop layout a second time.
        Assert.Equal(width < 640, vm.IsNarrow);
        return (vm, window);
    }

    /// <summary>
    /// Shows each category in turn, as someone checking an import would, so that every row's
    /// template is built and every binding has run. On a phone, picking a category opens it.
    /// </summary>
    private static void VisitEverySection(MasterDataEditorViewModel vm)
    {
        foreach (var section in vm.Sections.Concat<EditorSection>(vm.Checklists).ToList())
        {
            vm.SelectedSection = section;
            Dispatcher.UIThread.RunJobs();

            // An Objekt's Wohnungsbezeichnungen are behind a toggle; open each grid while it is shown.
            if (section is PremisesSection premises)
            {
                foreach (var row in premises.Rows)
                {
                    row.ToggleLabelGridCommand.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                }
            }

            vm.BackToCategoriesCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
