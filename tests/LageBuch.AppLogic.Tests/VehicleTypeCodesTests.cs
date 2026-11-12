using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// Tester feedback on v0.10.0-beta.1: the Fahrzeugtyp follows from the Kennung in the Funkrufname,
// so it is mapped once in the Stammdaten instead of picked per vehicle from an unsorted list that
// lacked the MZF.
public class VehicleTypeCodesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private readonly List<string> _questions = [];
    private int _changes;
    private Action? _confirm;

    private VehicleTypeCodesSection Section(params VehicleTypeCode[] codes) =>
        new("Fahrzeugkennungen", codes, () => _changes++, (question, remove) =>
        {
            _questions.Add(question);
            _confirm = remove;
        });

    private static VehicleRow Row(string callSign, string? vehicleType, Func<string, string?> typeFor) =>
        new("FFB Wache 1", callSign, 9, false, true, null, null, vehicleType, [], [], () => { }, typeFor);

    private static string? Mzf(string callSign) => VehicleTypeCode.TypeFor(callSign, [new VehicleTypeCode("12", "MZF")]);

    // --- the section --------------------------------------------------------------------------
    [Fact]
    public void Rows_are_saved_trimmed_only_with_both_fields_and_in_the_catalogue_s_spelling()
    {
        var section = Section();
        section.Rows.Add(new VehicleTypeCodeRow(" 12 ", " mzf ", () => { }));
        section.Rows.Add(new VehicleTypeCodeRow("44", " LF 20 ", () => { }));
        section.Rows.Add(new VehicleTypeCodeRow("10", " ", () => { }));
        section.Rows.Add(new VehicleTypeCodeRow(" ", "KdoW", () => { }));
        section.Rows.Add(new VehicleTypeCodeRow("11", null, () => { }));

        Assert.Equal([new VehicleTypeCode("12", "MZF"), new VehicleTypeCode("44", "LF 20")], section.ToValues());
    }

    [Fact]
    public void A_fahrzeugtyp_over_the_cap_is_cut_to_it()
    {
        var section = Section();
        section.Rows.Add(new VehicleTypeCodeRow("12", new string('X', VehicleTypes.MaxLength + 4), () => { }));

        Assert.Equal(new string('X', VehicleTypes.MaxLength), Assert.Single(section.ToValues()).VehicleType);
    }

    [Fact]
    public void Adding_appends_a_blank_row_and_marks_the_editor_changed()
    {
        var section = Section(new VehicleTypeCode("12", "MZF"));
        object? added = null;
        section.RowAdded += (_, e) => added = e.Row;

        section.AddCommand.Execute(null);

        Assert.Equal(2, section.Rows.Count);
        Assert.Same(section.Rows[1], added);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void A_blank_row_just_added_goes_without_asking()
    {
        var section = Section();
        section.AddCommand.Execute(null);

        section.RemoveCommand.Execute(section.Rows[0]);

        Assert.Empty(section.Rows);
        Assert.Empty(_questions);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void A_stored_row_goes_only_once_the_question_is_answered()
    {
        var section = Section(new VehicleTypeCode("12", "MZF"));

        section.RemoveCommand.Execute(section.Rows[0]);

        Assert.Equal(["„12“ aus Fahrzeugkennungen entfernen?"], _questions);
        Assert.Single(section.Rows);
        Assert.Equal(0, _changes);

        Assert.NotNull(_confirm);
        _confirm();

        Assert.Empty(section.Rows);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void A_row_that_is_not_in_the_section_is_left_alone()
    {
        var section = Section(new VehicleTypeCode("12", "MZF"));

        section.RemoveCommand.Execute(new VehicleTypeCodeRow("12", "MZF", () => { }));

        Assert.Single(section.Rows);
        Assert.Empty(_questions);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Typing_in_a_row_marks_the_editor_changed()
    {
        var section = Section(new VehicleTypeCode("12", "MZF"));

        section.Rows[0].Kennung = "13";
        section.Rows[0].VehicleType = "MTW";

        Assert.Equal(2, _changes);
    }

    // --- the Fahrzeuge rows -------------------------------------------------------------------
    [Fact]
    public void Typing_a_funkrufname_fills_an_empty_fahrzeugtyp_from_its_kennung()
    {
        var row = Row(string.Empty, null, Mzf);

        row.CallSign = "FFB 1/12/1";

        Assert.Equal("MZF", row.VehicleType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_fahrzeugtyp_counts_as_empty(string blank)
    {
        var row = Row(string.Empty, blank, Mzf);

        row.CallSign = "FFB 1/12/1";

        Assert.Equal("MZF", row.VehicleType);
    }

    [Fact]
    public void A_chosen_fahrzeugtyp_is_never_overwritten()
    {
        var row = Row("FFB 1/40/1", "HLF", Mzf);

        row.CallSign = "FFB 1/12/1";

        Assert.Equal("HLF", row.VehicleType);
    }

    [Fact]
    public void A_funkrufname_without_a_listed_kennung_leaves_the_fahrzeugtyp_empty()
    {
        var row = Row(string.Empty, null, Mzf);

        row.CallSign = "FFB 1/99/1";

        Assert.Null(row.VehicleType);
    }

    [Fact]
    public void A_row_without_the_lookup_fills_nothing()
    {
        var row = new VehicleRow("FFB Wache 1", string.Empty, 9, false, true, null, null, null, [], [], () => { });

        row.CallSign = "FFB 1/12/1";

        Assert.Null(row.VehicleType);
    }

    [Fact]
    public void The_vehicles_section_hands_the_lookup_to_rows_loaded_and_added()
    {
        var section = new VehiclesSection("Fahrzeuge", [new Vehicle("FFB Wache 1", "FFB 1/40/1", 9)], [], [], () => { }, typeForCallSign: Mzf);
        section.AddCommand.Execute(null);

        section.Rows[0].CallSign = "FFB 1/12/1";
        section.Rows[1].CallSign = "FFB 1/12/2";

        Assert.Equal(["MZF", "MZF"], section.Rows.Select(r => r.VehicleType));
    }

    // The lookup reads the Fahrzeugkennungen as they stand in the editor, not as they were loaded,
    // so a Kennung entered a moment ago already fills the next vehicle.
    [Fact]
    public void The_editor_fills_a_new_vehicle_from_a_kennung_entered_but_not_yet_saved()
    {
        var vm = new MasterDataEditorViewModel(new Provider(MasterDataSet.Empty), new FakeDialogs(), new NoFiles());
        var codes = vm.Sections.OfType<VehicleTypeCodesSection>().Single();
        var vehicles = vm.Sections.OfType<VehiclesSection>().Single();

        codes.AddCommand.Execute(null);
        codes.Rows[0].Kennung = "12";
        codes.Rows[0].VehicleType = "MZF";
        vehicles.AddCommand.Execute(null);
        vehicles.Rows[0].Wache = "FFB Wache 1";
        vehicles.Rows[0].CallSign = "FFB 1/12/1";

        Assert.Equal("MZF", vehicles.Rows[0].VehicleType);
    }

    [Fact]
    public void Saving_the_editor_stores_the_kennungen()
    {
        var provider = new Provider(MasterDataSet.Empty with { VehicleTypeCodes = [new VehicleTypeCode("12", "MZF")] });
        var vm = new MasterDataEditorViewModel(provider, new FakeDialogs(), new NoFiles());
        var codes = vm.Sections.OfType<VehicleTypeCodesSection>().Single();

        codes.Rows[0].VehicleType = "MTW";
        vm.SaveCommand.Execute(null);

        Assert.Equal([new VehicleTypeCode("12", "MTW")], provider.Get().VehicleTypeCodes);
    }

    // --- the Fahrzeugtyp pickers --------------------------------------------------------------
    [Fact]
    public void The_catalogue_knows_the_mzf()
    {
        Assert.Equal("Mehrzweckfahrzeug", VehicleTypes.Find("mzf")?.Name);
    }

    [Fact]
    public void The_pickers_offer_the_catalogue_alphabetically_with_ohne_typ_first()
    {
        Assert.Equal(
            [
                VehicleTypeChoices.None, "DLK", "ELW 1", "ELW 2", "GW-A", "GW-G", "GW-L", "HLF", "KdoW", "KTW",
                "LF", "MTW", "MZF", "NEF", "RTW", "RW", "SW", "TLF", "TSF", "WLF",
            ],
            VehicleTypeChoices.For(null));
        Assert.Equal(VehicleTypeChoices.For(null).Skip(1), VehicleRow.VehicleTypeOptions);
    }

    [Fact]
    public void A_recorded_type_the_catalogue_lacks_still_follows_ohne_typ()
    {
        var options = VehicleTypeChoices.For("TLF 4000");

        Assert.Equal([VehicleTypeChoices.None, "TLF 4000", "DLK"], options.Take(3));
        Assert.Equal(VehicleTypes.All.Count + 2, options.Count);
    }

    // --- the Kräfte form ----------------------------------------------------------------------
    private static ForcesViewModel Forces(MasterDataSet masterData) =>
        new(
            TestSession.StartNew(new FakeStore(), new FixedClock(T0), new SessionOperator("Müller"), "/x.lagebuch", [], []),
            new FixedClock(T0),
            masterData,
            () => { });

    [Fact]
    public void The_fahrzeug_picker_reads_by_wache_then_funkrufname_numbers_by_value()
    {
        using var vm = Forces(MasterDataSet.Empty with
        {
            Vehicles =
            [
                new Vehicle("FFB Wache 10", "FFB 10/44/1", 9),
                new Vehicle("FFB Wache 2", "FFB 2/44/1", 9),
                new Vehicle("Aich", "Aich 42/1", 9),
                new Vehicle("FFB Wache 2", "FFB 2/11/1", 4),
                new Vehicle("FFB Wache 2", "FFB 2/44/10", 9),
                new Vehicle("FFB Wache 2", "FFB 2/44/2", 9),
            ],
        });

        Assert.Equal(
            ["Aich 42/1", "FFB 2/11/1", "FFB 2/44/1", "FFB 2/44/2", "FFB 2/44/10", "FFB 10/44/1"],
            vm.VehicleOptions.Select(v => v.CallSign));
    }

    [Fact]
    public void Picking_a_fahrzeug_without_a_type_takes_the_one_of_its_kennung()
    {
        using var vm = Forces(MasterDataSet.Empty with
        {
            Vehicles = [new Vehicle("FFB Wache 1", "FFB 1/12/1", 6)],
            VehicleTypeCodes = [new VehicleTypeCode("12", "MZF")],
        });

        vm.SelectedVehicle = vm.VehicleOptions.Single();

        Assert.Equal("MZF", vm.NewVehicleType);
    }

    [Fact]
    public void Picking_a_fahrzeug_keeps_the_type_the_stammdaten_give_it()
    {
        using var vm = Forces(MasterDataSet.Empty with
        {
            Vehicles = [new Vehicle("FFB Wache 1", "FFB 1/12/1", 6, VehicleType: "MTW")],
            VehicleTypeCodes = [new VehicleTypeCode("12", "MZF")],
        });

        vm.SelectedVehicle = vm.VehicleOptions.Single();

        Assert.Equal("MTW", vm.NewVehicleType);
    }

    private sealed class Provider(MasterDataSet initial) : IMasterDataProvider
    {
        private MasterDataSet _set = initial;

        public MasterDataSet Get() => _set;

        public void Save(MasterDataSet set) => _set = set;
    }
}
