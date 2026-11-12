using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #410: the Gasmessung with eight gases, on a desktop and on a phone, and the Messgase tab of the
// Stammdaten. Screenshots land in RENDER_OUT for the pull request.
public class GasmessungRenderTests
{
    private static readonly GasDefinition[] EightGases =
    [
        GasDefinition.DefaultCo,
        new("CO2", "Vol-%", GasDirection.Rising, 0.5m, 4m, 8m, 20m),
        new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m),
        new("CH4", "%UEG", GasDirection.Rising, 10m, 20m, 40m, 100m),
        new("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m),
        new("HCN", "ppm", GasDirection.Rising, 2m, 10m, 50m, 100m),
        new("NH3", "ppm", GasDirection.Rising, 20m, 50m, 300m, 1000m),
        new("Cl2", "ppm", GasDirection.Rising, 0.5m, 1m, 10m, 50m),
    ];

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, []);

        public void Write(string path, MasterDataSet set)
        {
        }
    }

    private sealed class Provider(IReadOnlyList<GasDefinition>? gases = null) : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with { Gases = gases ?? EightGases.Take(5).ToList() };

        public void Save(MasterDataSet set)
        {
        }
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

    private static (Window Window, IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm) Workspace(double width, double height)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(gases: EightGases);
        var co = vm.CoMessprotokoll;
        co.NewBuildingName = "Hauptstraße 12";
        co.NewBuildingFloors = 2;
        co.NewBuildingApartments = 3;
        co.NewBuildingUndergroundFloors = 0;
        co.ConfirmAddBuildingCommand.Execute(null);

        // A measured Wohnung with every band on show: CO erhöht, H2S gefährlich, O2 lebensgefährlich.
        var cell = co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0];
        cell.OpenEditorCommand.Execute(null);
        co.Editor!.GasInputs[0].Value = 45;
        co.Editor.GasInputs[2].Value = 12;
        co.Editor.GasInputs[4].Value = 9.5m;
        co.ConfirmEditorCommand.Execute(null);

        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    [AvaloniaFact]
    public void The_tile_shows_the_worst_of_eight_gases()
    {
        var (window, _, vm) = Workspace(1280, 800);
        Capture(window, "gasmessung-kachel.png");

        var cell = vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 2).Cells[0];
        Assert.Equal("O2 9,5 Vol-%", cell.CoCompact);
        Assert.True(cell.IsCoLethal);
    }

    [AvaloniaFact]
    public void The_sidebar_holds_eight_inputs_and_FERTIG_stays_reachable()
    {
        var (window, view, vm) = Workspace(1920, 1080);
        vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "gasmessung-sidebar.png");

        var inputs = view.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.Name == "GasValueInput").ToList();
        Assert.Equal(8, inputs.Count);
        Assert.All(inputs, i => Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(i))));

        var sidebar = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "DwellingEditor");
        var confirm = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EditorConfirmButton");
        var bottom = confirm.TranslatePoint(new Point(0, confirm.Bounds.Height), sidebar)!.Value.Y;
        Assert.True(bottom <= sidebar.Bounds.Height, $"FERTIG ends at y={bottom:0} in a {sidebar.Bounds.Height:0}px sidebar");
    }

    [AvaloniaFact]
    public void Eight_inputs_fit_a_phone_without_running_sideways()
    {
        var (window, view, vm) = Workspace(412, 915);
        vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "gasmessung-phone.png");

        // The Wohnung editor only: the summary strip's progress track is a fixed 420px and runs
        // past a phone whenever a Haus exists, with one gas or eight -- older than #410, and its
        // own fix.
        var editor = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "DwellingEditor");
        var offenders = editor.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.TemplatedParent is null)
            .Where(c => c.TranslatePoint(new Point(c.Bounds.Width, 0), window)!.Value.X > 413)
            .Select(c => $"{c.Name ?? c.GetType().Name} ends at x={c.TranslatePoint(new Point(c.Bounds.Width, 0), window)!.Value.X:0}")
            .Take(8)
            .ToList();
        Assert.True(offenders.Count == 0, $"these run past the phone: {string.Join(", ", offenders)}");
    }

    [AvaloniaFact]
    public void The_Messgase_tab_renders_each_gas_with_its_bands()
    {
        var vm = new MasterDataEditorViewModel(new Provider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Messgase");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(window, "stammdaten-messgase.png");

        var names = view.GetVisualDescendants().OfType<TextBox>()
            .Where(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Name des Messgases")
            .Select(t => t.Text)
            .ToList();
        Assert.Equal(new[] { "CO", "CO2", "H2S", "CH4", "O2" }, names);
    }

    // Feedback from v0.10.0-beta.1: a second O2 greyed SPEICHERN out, and the only word about it
    // was a badge at the far left of the header. Now the section says it, the two rows are marked
    // and the disabled button names the reason.
    [AvaloniaFact]
    public void A_gas_entered_twice_is_named_in_the_section_and_on_its_rows()
    {
        var vm = new MasterDataEditorViewModel(new Provider(), new FakeDialogs(), new NoFiles());
        var section = Assert.IsType<GasesSection>(vm.Sections.Single(s => s.Title == "Messgase"));
        vm.SelectedSection = section;
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var message = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "GasSectionConflicts");
        Assert.False(message.IsVisible);

        section.Rows[1].Name = "O2";
        Dispatcher.UIThread.RunJobs();
        Capture(window, "stammdaten-messgase-doppelt.png");

        Assert.True(message.IsEffectivelyVisible);
        Assert.Equal("Messgas O2 ist doppelt eingetragen\nSpeichern erst nach Korrektur möglich", message.Text);
        var marked = view.GetVisualDescendants().OfType<Grid>()
            .Where(g => g.Name == "GasRowGrid")
            .Select(g => g.Classes.Contains("invalid"))
            .ToList();
        Assert.Equal(new[] { false, true, false, false, true }, marked);
        var error = view.FindResource("ErrorBrush");
        var names = view.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "GasNameInput").ToList();
        Assert.Same(error, names[1].BorderBrush);
        Assert.NotSame(error, names[0].BorderBrush);

        var save = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton");
        Assert.False(save.IsEffectivelyEnabled);
        Assert.Equal("Erst korrigieren:\nMessgas O2 ist doppelt eingetragen", ToolTip.GetTip(save));
        Assert.True(ToolTip.GetShowOnDisabled(save));
    }

    // Feedback from v0.10.0-beta.1: the unit field looked like a plain box, though it knows ppm and
    // Vol-%. Opened on a row that already says Vol-%, it lists every unit, not just that one.
    [AvaloniaFact]
    public void The_unit_field_lists_every_unit_whatever_it_already_holds()
    {
        var vm = new MasterDataEditorViewModel(new Provider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Messgase");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var unit = view.GetVisualDescendants().OfType<AutoCompleteBox>().Where(b => b.Name == "GasUnitInput").ElementAt(1);
        Assert.Equal("Vol-%", unit.Text);
        unit.Focus();
        unit.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        var popup = unit.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "PART_Popup");
        var list = popup.GetLogicalDescendants().OfType<SelectingItemsControl>().Single();
        Assert.True(unit.IsDropDownOpen);
        Assert.Equal(GasDefinition.UnitSuggestions, list.Items.Cast<string>());
    }

    // #927, feedback from v0.10.0-beta.1: O2 could not be entered with a falling and a rising
    // alarm. A falling gas now has „Anreicherung ab“; a rising one does not show the field.
    [AvaloniaFact]
    public void Only_the_falling_gas_offers_Anreicherung_ab_and_it_follows_the_direction()
    {
        var o2 = new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m, UpperAlarm: 23m);
        var vm = new MasterDataEditorViewModel(new Provider([GasDefinition.DefaultCo, o2]), new FakeDialogs(), new NoFiles());
        var section = Assert.IsType<GasesSection>(vm.Sections.Single(s => s.Title == "Messgase"));
        vm.SelectedSection = section;
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(window, "stammdaten-messgase-anreicherung.png");

        var inputs = view.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.Name == "GasUpperAlarmInput").ToList();
        Assert.Equal(new[] { false, true }, inputs.Select(i => i.IsEffectivelyVisible));
        Assert.Equal(23m, inputs[1].Value);
        Assert.Equal("Anreicherung ab", Avalonia.Automation.AutomationProperties.GetName(inputs[1]));

        // A rising row keeps the slot, so its fields stay under the same headers as the O2 row's.
        var units = view.GetVisualDescendants().OfType<AutoCompleteBox>().Where(b => b.Name == "GasUnitInput").ToList();
        var x = units.Select(u => u.TranslatePoint(new Point(0, 0), view)!.Value.X).ToList();
        Assert.True(Math.Abs(x[0] - x[1]) < 1, $"EINHEIT starts at x={x[0]:0} on CO and x={x[1]:0} on O2");

        section.Rows[0].Direction = GasDirection.Falling;
        Dispatcher.UIThread.RunJobs();

        Assert.True(inputs[0].IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Anreicherung_ab_fits_a_phone()
    {
        var o2 = new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m, UpperAlarm: 23m);
        var vm = new MasterDataEditorViewModel(new Provider([GasDefinition.DefaultCo, o2]), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 412, Height = 915 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // On a phone, picking the category steps into it.
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Messgase");
        Dispatcher.UIThread.RunJobs();
        Capture(window, "stammdaten-messgase-anreicherung-phone.png");

        var input = view.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.Name == "GasUpperAlarmInput").ElementAt(1);
        Assert.True(input.IsEffectivelyVisible);
        var right = input.TranslatePoint(new Point(input.Bounds.Width, 0), window)!.Value.X;
        Assert.True(right <= 412, $"Anreicherung ab ends at x={right:0}");
    }

    [AvaloniaFact]
    public void An_enriched_O2_reading_turns_the_tile_dangerous()
    {
        var o2 = new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m, UpperAlarm: 23m);
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(gases: [GasDefinition.DefaultCo, o2]);
        var co = vm.CoMessprotokoll;
        co.NewBuildingName = "Hauptstraße 12";
        co.NewBuildingFloors = 2;
        co.NewBuildingApartments = 3;
        co.NewBuildingUndergroundFloors = 0;
        co.ConfirmAddBuildingCommand.Execute(null);
        var cell = co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0];
        cell.OpenEditorCommand.Execute(null);
        co.Editor!.GasInputs[0].Value = 5;
        co.Editor.GasInputs[1].Value = 24;
        co.ConfirmEditorCommand.Execute(null);
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");
        Dispatcher.UIThread.RunJobs();
        Capture(window, "gasmessung-anreicherung.png");

        Assert.Equal("O2 24 Vol-%", cell.CoCompact);
        Assert.True(cell.IsCoDangerous);
    }
}
