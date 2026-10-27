using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;

namespace LageBuch.AppLogic.Tests;

// #410: the Gasmessung with several gases -- the tile shows the worst one, the sidebar one input
// per gas, FERTIG one ETB entry for all of them.
public class GasmessungViewModelTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 27, 8, 14, 0, TimeSpan.Zero));

    private static readonly GasDefinition H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);
    private static readonly GasDefinition O2 = new("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);

    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm) CreateVm(params GasDefinition[] stammdaten)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator("Huber", null),
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        var vm = new CoMessprotokollViewModel(session, Clock, () => { }, gases: stammdaten);
        vm.NewBuildingName = "Haus A";
        vm.NewBuildingFloors = 2;
        vm.NewBuildingApartments = 3;
        vm.NewBuildingUndergroundFloors = 0;
        vm.ConfirmAddBuildingCommand.Execute(null);
        return (session, vm);
    }

    private static DwellingCellViewModel Cell(CoMessprotokollViewModel vm, int floor, int apartment) =>
        vm.MatrixRows.SelectMany(r => r.Cells).First(c => c.FloorOrdinal == floor && c.ApartmentNumber == apartment);

    private static DwellingEditorViewModel OpenEditor(CoMessprotokollViewModel vm, int floor, int apartment)
    {
        Cell(vm, floor, apartment).OpenEditorCommand.Execute(null);
        return vm.Editor!;
    }

    [Fact]
    public void The_first_Haus_copies_the_Stammdaten_gases_into_the_Einsatz()
    {
        var (session, _) = CreateVm(GasDefinition.DefaultCo, H2S, O2);

        Assert.Equal(new[] { "CO", "H2S", "O2" }, session.Incident.Gases.Select(g => g.Name));
    }

    [Fact]
    public void The_sidebar_has_one_input_per_gas_headed_by_its_name()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, H2S, O2);

        var editor = OpenEditor(vm, 0, 1);

        Assert.Equal(new[] { "CO", "H2S", "O2" }, editor.GasInputs.Select(g => g.Header));
        Assert.Equal("O2-Wert", editor.GasInputs[2].AccessibleName);
    }

    [Fact]
    public void FERTIG_records_every_changed_gas_in_one_ETB_entry()
    {
        var (session, vm) = CreateVm(GasDefinition.DefaultCo, H2S, O2);
        var editor = OpenEditor(vm, 2, 1);
        editor.GasInputs[0].Value = 120;
        editor.GasInputs[1].Value = 3;
        var journalBefore = session.Incident.Journal.Count;

        vm.ConfirmEditorCommand.Execute(null);

        var measurements = session.Incident.Journal.Skip(journalBefore).Where(e => e.Direction == EtbDirection.Measurement).ToList();
        Assert.Equal("Gasmessung Haus A, 2. OG, Links: CO 120 ppm, H2S 3 ppm", Assert.Single(measurements).Text);
    }

    [Fact]
    public void FERTIG_with_no_gas_changed_records_no_measurement()
    {
        var (session, vm) = CreateVm(GasDefinition.DefaultCo, H2S);
        var editor = OpenEditor(vm, 0, 1);
        editor.GasInputs[1].Value = 3;
        editor.GasInputs[1].Value = null;
        var journalBefore = session.Incident.Journal.Count;

        vm.ConfirmEditorCommand.Execute(null);

        Assert.Equal(journalBefore, session.Incident.Journal.Count);
    }

    [Fact]
    public void A_decimal_O2_value_is_committed_and_shown_the_German_way()
    {
        var (session, vm) = CreateVm(O2);
        OpenEditor(vm, 0, 1).GasInputs[0].Value = 20.9m;

        vm.ConfirmEditorCommand.Execute(null);

        Assert.Equal(20.9m, session.Incident.Dwellings.First(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).ValueOf(session.Incident.Gases[0].Id));
        Assert.Equal("20,9 Vol-%", Cell(vm, 0, 1).CoCompact);
    }

    [Fact]
    public void The_tile_shows_the_worst_gas_by_name_and_takes_its_colour()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, H2S, O2);
        var editor = OpenEditor(vm, 0, 1);
        editor.GasInputs[0].Value = 40; // CO Erhöht
        editor.GasInputs[1].Value = 12; // H2S Gefährlich
        editor.GasInputs[2].Value = 20.9m; // O2 normal
        vm.ConfirmEditorCommand.Execute(null);

        var cell = Cell(vm, 0, 1);

        Assert.Equal("H2S 12 ppm", cell.CoCompact);
        Assert.True(cell.IsCoDangerous);
        Assert.False(cell.IsCoElevated);
    }

    [Fact]
    public void A_falling_O2_is_the_worst_gas_once_it_drops_below_its_band()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, O2);
        var editor = OpenEditor(vm, 0, 1);
        editor.GasInputs[0].Value = 40; // CO Erhöht
        editor.GasInputs[1].Value = 9; // O2 Lebensgefährlich
        vm.ConfirmEditorCommand.Execute(null);

        var cell = Cell(vm, 0, 1);

        Assert.Equal("O2 9 Vol-%", cell.CoCompact);
        Assert.True(cell.IsCoLethal);
    }

    [Fact]
    public void A_CO_only_Einsatz_reads_exactly_as_before()
    {
        var (_, vm) = CreateVm();
        OpenEditor(vm, 0, 1).GasInputs[0].Value = 45;
        vm.ConfirmEditorCommand.Execute(null);

        var cell = Cell(vm, 0, 1);

        Assert.Equal("45 ppm", cell.CoCompact);
        Assert.Equal("45 ppm", cell.CoDisplay);
        Assert.Equal("CO", OpenEditor(vm, 0, 1).GasInputs.Single().Header);
    }

    [Fact]
    public void The_tooltip_lists_every_gas_each_with_its_own_band()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, H2S);
        var editor = OpenEditor(vm, 0, 1);
        editor.GasInputs[0].Value = 250;
        editor.GasInputs[1].Value = 1;
        vm.ConfirmEditorCommand.Execute(null);

        var rows = Cell(vm, 0, 1).GasValueRows;

        Assert.Equal(new[] { "CO 250 ppm", "H2S 1 ppm" }, rows.Select(r => r.Text));
        Assert.True(rows[0].IsCoDangerous);
        Assert.False(rows[1].IsCoElevated || rows[1].IsCoDangerous || rows[1].IsCoLethal);
        Assert.Contains("CO 250 ppm · H2S 1 ppm", Cell(vm, 0, 1).TileTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_value_previews_on_the_tile_before_FERTIG()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, H2S);
        var editor = OpenEditor(vm, 0, 1);

        editor.GasInputs[1].Value = 12;

        Assert.Equal("H2S 12 ppm", Cell(vm, 0, 1).CoCompact);
        Assert.True(editor.HasChanges);
    }

    [Fact]
    public void Each_input_warns_about_its_own_implausible_value()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, O2);
        var editor = OpenEditor(vm, 0, 1);

        editor.GasInputs[1].Value = 209;

        Assert.False(editor.GasInputs[0].IsImplausible);
        Assert.True(editor.GasInputs[1].IsImplausible);
        Assert.True(editor.IsCoImplausible);
    }

    [Fact]
    public void A_Messreihe_row_names_the_gas_when_there_are_several()
    {
        var (_, vm) = CreateVm(GasDefinition.DefaultCo, H2S);
        var editor = OpenEditor(vm, 0, 1);
        editor.GasInputs[1].Value = 3;
        vm.ConfirmEditorCommand.Execute(null);

        var row = Assert.Single(OpenEditor(vm, 0, 1).Readings);

        Assert.Equal("H2S", row.Gas);
        Assert.Equal("3 ppm", row.Value);
    }

    [Fact]
    public void A_tile_announces_what_each_change_redraws()
    {
        var (session, vm) = CreateVm(GasDefinition.DefaultCo, H2S);
        var cell = Cell(vm, 0, 1);
        var changed = new List<string?>();
        cell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        string[] tooltip = [nameof(cell.TooltipHead), nameof(cell.TooltipTail), nameof(cell.TileTooltip)];

        cell.Status = DwellingStatus.Affected;
        Assert.Superset(new HashSet<string?>([nameof(cell.StatusGlyph), .. tooltip]), changed.ToHashSet());

        changed.Clear();
        cell.Values = new Dictionary<Guid, decimal> { [session.Incident.Gases[0].Id] = 1 };
        Assert.Superset(
            new HashSet<string?>([
                nameof(cell.Worst), nameof(cell.GasValueRows), nameof(cell.CoDisplay), nameof(cell.CoCompact),
                nameof(cell.IsCoElevated), nameof(cell.IsCoDangerous), nameof(cell.IsCoLethal), .. tooltip]),
            changed.ToHashSet());

        changed.Clear();
        cell.KeyAvailable = KeyAvailability.Available;
        Assert.Superset(new HashSet<string?>([nameof(cell.KeyDisplay), .. tooltip]), changed.ToHashSet());

        changed.Clear();
        cell.KeyNumber = "12";
        Assert.Superset(new HashSet<string?>([nameof(cell.KeyDisplay), .. tooltip]), changed.ToHashSet());
    }

    [Fact]
    public void A_tile_and_a_sidebar_built_without_gases_show_none()
    {
        var building = Building.Create("Haus A", 1, 1, 0);
        var dwelling = Dwelling.Rehydrate(Guid.NewGuid(), building.Id, 0, 1, null, DwellingStatus.NotSearched, KeyAvailability.Unknown, null);

        var cell = new DwellingCellViewModel(dwelling, building, isReadOnly: false, (_, _, _) => { });
        var editor = new DwellingEditorViewModel(dwelling, building);

        Assert.Empty(cell.GasValueRows);
        Assert.Empty(editor.GasInputs);
    }

    [Fact]
    public void A_Messreihe_row_of_a_gas_the_Einsatz_lacks_shows_the_bare_number_uncoloured()
    {
        List<IncidentGas> gases = [new(Guid.NewGuid(), GasDefinition.DefaultCo), new(Guid.NewGuid(), H2S)];

        var row = CoReadingRow.From(new CoReading(Clock.Now, Guid.NewGuid(), 7, "x"), gases);

        Assert.Equal("7", row.Value);
        Assert.Equal(string.Empty, row.Gas);
        Assert.False(row.IsCoElevated || row.IsCoDangerous || row.IsCoLethal);
    }

    [Fact]
    public void A_Messreihe_row_of_a_CO_only_Einsatz_names_no_gas_and_flags_a_lethal_value()
    {
        List<IncidentGas> gases = [new(Guid.NewGuid(), GasDefinition.DefaultCo)];

        var row = CoReadingRow.From(new CoReading(Clock.Now, gases[0].Id, 2500, "x"), gases);

        Assert.Equal(string.Empty, row.Gas);
        Assert.True(row.IsCoLethal);
        Assert.False(row.IsCoDangerous);
    }

    [Fact]
    public void An_input_rounds_what_FERTIG_would_record_and_colours_its_band()
    {
        var input = new GasInputRow(new IncidentGas(Guid.NewGuid(), GasDefinition.DefaultCo), null, () => { });

        input.Value = 40.555m;
        Assert.Equal(40.56m, input.Committed);
        Assert.True(input.IsCoElevated);
        Assert.False(input.IsCoDangerous);

        input.Value = 250;
        Assert.True(input.IsCoDangerous);
        Assert.False(input.IsCoElevated);
    }

    [Fact]
    public void A_tile_whose_Haus_is_gone_opens_no_sidebar()
    {
        var (session, vm) = CreateVm();
        var cell = Cell(vm, 0, 1);
        session.RemoveCoBuilding(session.Incident.Buildings[0].Id);

        cell.OpenEditorCommand.Execute(null);

        Assert.Null(vm.Editor);
    }

    [Fact]
    public void Shrinking_past_a_Wohnung_whose_value_was_cleared_names_its_Messreihe()
    {
        var (session, vm) = CreateVm();
        var haus = session.Incident.Buildings[0].Id;
        var co = session.Incident.Gases[0].Id;
        session.RecordCoValues(haus, 0, 3, new Dictionary<Guid, decimal?> { [co] = 40 });
        session.RecordCoValues(haus, 0, 3, new Dictionary<Guid, decimal?> { [co] = null });

        vm.MatrixRows.Single(r => r.Ordinal == 0).ApartmentCount = 2;

        var pending = vm.PendingApartmentRemoval;
        Assert.NotNull(pending);
        Assert.Contains("Messreihe", pending.Rows.Single(r => r.ApartmentNumber == 3).Contents, StringComparison.Ordinal);
    }
}
