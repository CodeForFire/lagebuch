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
}
