using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// #410: the Messgase section of the Stammdaten editor.
public class GasesSectionTests
{
    private static readonly GasDefinition H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);

    private sealed class Provider(MasterDataSet set) : IMasterDataProvider
    {
        public MasterDataSet Saved { get; private set; } = set;

        public MasterDataSet Get() => Saved;

        public void Save(MasterDataSet set) => Saved = set;
    }

    private static readonly Dictionary<string, Action<GasRow>> Fill = new(StringComparer.Ordinal)
    {
        ["name"] = r => r.Name = "H2S",
        ["unit"] = r => r.Unit = "ppm",
        ["elevated"] = r => r.Elevated = 5,
        ["dangerous"] = r => r.Dangerous = 10,
        ["lethal"] = r => r.Lethal = 100,
        ["plausible"] = r => r.PlausibleUpTo = 500,
    };

    private static GasesSection Section(params GasDefinition[] gases) => new("Messgase", gases, () => { });

    [Fact]
    public void Add_stops_at_eight_gases()
    {
        var section = Section(GasDefinition.DefaultCo);

        for (var i = 0; i < 10; i++)
        {
            if (section.AddCommand.CanExecute(null))
            {
                section.AddCommand.Execute(null);
            }
        }

        Assert.Equal(GasDefinition.MaxCount, section.Rows.Count);
        Assert.False(section.AddCommand.CanExecute(null));
    }

    [Fact]
    public void The_last_gas_cannot_be_removed()
    {
        var section = Section(GasDefinition.DefaultCo, H2S);

        section.RemoveCommand.Execute(section.Rows[1]);

        Assert.Single(section.Rows);
        Assert.False(section.RemoveCommand.CanExecute(section.Rows[0]));
        section.RemoveCommand.Execute(section.Rows[0]);
        Assert.Single(section.Rows);
    }

    [Fact]
    public void Thresholds_out_of_order_are_named_as_a_conflict()
    {
        var section = Section(GasDefinition.DefaultCo);
        section.Rows[0].Dangerous = 10; // below Erhöht 30

        Assert.Equal("Schwellen nicht in Reihenfolge: CO", Assert.Single(section.Conflicts()));
    }

    [Fact]
    public void A_falling_gas_wants_its_bands_falling()
    {
        var section = Section(new GasDefinition("O2", "Vol-%", GasDirection.Rising, 19m, 17m, 10m, null));
        Assert.NotEmpty(section.Conflicts());

        section.Rows[0].Direction = GasDirection.Falling;

        Assert.Empty(section.Conflicts());
    }

    [Fact]
    public void A_nameless_row_and_a_name_used_twice_are_named_as_conflicts()
    {
        var section = Section(GasDefinition.DefaultCo, H2S);
        section.AddCommand.Execute(null);
        section.Rows[1].Name = "co";

        var conflicts = section.Conflicts();

        Assert.Contains("Messgas ohne Namen: Zeile 3", conflicts);
        Assert.Contains("Doppelte Messgase: CO", conflicts);
    }

    [Fact]
    public void Values_come_back_trimmed_in_row_order()
    {
        var section = Section(H2S, GasDefinition.DefaultCo);
        section.Rows[0].Unit = "  ppm ";

        Assert.Equal(new[] { H2S, GasDefinition.DefaultCo }, section.ToValues());
    }

    [Fact]
    public void A_conflict_keeps_the_editor_from_saving_and_fixing_it_lets_it_save()
    {
        var provider = new Provider(MasterDataSet.Empty with { Gases = [GasDefinition.DefaultCo] });
        var vm = new MasterDataEditorViewModel(provider, new FakeDialogs(), new NoFiles());
        var gases = vm.Sections.OfType<GasesSection>().Single();

        gases.Rows[0].Lethal = 100; // below Gefährlich 200

        Assert.True(vm.HasGasConflicts);
        Assert.False(vm.SaveCommand.CanExecute(null));

        gases.Rows[0].Lethal = 900;

        Assert.False(vm.HasGasConflicts);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Equal(900m, Assert.Single(provider.Saved.Gases).Lethal);
    }

    [Fact]
    public void Adding_a_row_rechecks_both_buttons_marks_the_editor_changed_and_hands_the_row_to_the_view()
    {
        var changes = 0;
        var section = new GasesSection("Messgase", [GasDefinition.DefaultCo], () => changes++);
        var (addChecks, removeChecks) = (0, 0);
        section.AddCommand.CanExecuteChanged += (_, _) => addChecks++;
        section.RemoveCommand.CanExecuteChanged += (_, _) => removeChecks++;
        object? handedOver = null;
        section.RowAdded += (_, e) => handedOver = e.Row;

        section.AddCommand.Execute(null);

        Assert.True(addChecks > 0);
        Assert.True(removeChecks > 0);
        Assert.Equal(1, changes);
        Assert.Same(section.Rows[1], handedOver);
    }

    [Fact]
    public void Removing_no_row_or_a_row_from_elsewhere_does_nothing()
    {
        var asked = 0;
        var section = new GasesSection("Messgase", [GasDefinition.DefaultCo, H2S], () => { }, (_, _) => asked++);
        var stranger = Section(H2S).Rows[0];

        section.RemoveCommand.Execute(null);
        section.RemoveCommand.Execute(stranger);

        Assert.Equal(2, section.Rows.Count);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_row_added_and_left_blank_goes_at_once()
    {
        var (changes, asked) = (0, 0);
        var section = new GasesSection("Messgase", [GasDefinition.DefaultCo], () => changes++, (_, _) => asked++);
        section.AddCommand.Execute(null);

        section.RemoveCommand.Execute(section.Rows[1]);

        Assert.Single(section.Rows);
        Assert.Equal(0, asked);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("unit")]
    [InlineData("elevated")]
    [InlineData("dangerous")]
    [InlineData("lethal")]
    [InlineData("plausible")]
    public void A_row_added_with_any_field_filled_asks_before_it_goes(string field)
    {
        var asked = 0;
        var section = new GasesSection("Messgase", [GasDefinition.DefaultCo], () => { }, (_, _) => asked++);
        section.AddCommand.Execute(null);
        var row = section.Rows[1];
        Fill[field](row);

        section.RemoveCommand.Execute(row);

        Assert.Equal(1, asked);
        Assert.Equal(2, section.Rows.Count);
    }

    [Fact]
    public void Several_nameless_rows_are_named_together_and_not_as_duplicates()
    {
        var section = Section(GasDefinition.DefaultCo);
        section.AddCommand.Execute(null);
        section.AddCommand.Execute(null);

        Assert.Equal("Messgas ohne Namen: Zeilen 2, 3", Assert.Single(section.Conflicts()));
    }

    [Fact]
    public void A_list_of_nameless_rows_saves_as_CO_alone()
    {
        var section = Section(GasDefinition.DefaultCo);
        section.Rows[0].Name = "  ";

        Assert.Equal(new[] { GasDefinition.DefaultCo }, section.ToValues());
    }

    [Fact]
    public void A_stored_list_with_a_conflict_is_flagged_as_soon_as_the_editor_opens()
    {
        var provider = new Provider(MasterDataSet.Empty with { Gases = [GasDefinition.DefaultCo with { Lethal = 100 }] });

        var vm = new MasterDataEditorViewModel(provider, new FakeDialogs(), new NoFiles());

        Assert.True(vm.HasGasConflicts);
    }
}
