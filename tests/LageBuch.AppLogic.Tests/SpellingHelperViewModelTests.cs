using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Spelling;

namespace LageBuch.AppLogic.Tests;

public class SpellingHelperViewModelTests
{
    [Fact]
    public void Starts_on_the_traditional_table_with_nothing_spelled()
    {
        var vm = new SpellingHelperViewModel();

        Assert.True(vm.IsTraditional);
        Assert.False(vm.HasSpelling);
        Assert.Equal(string.Empty, vm.Spelled);
        Assert.Equal("Anton", vm.TableRows[0].Word);
    }

    [Fact]
    public void Choosing_another_alphabet_respells_the_word_and_swaps_the_table()
    {
        var vm = new SpellingHelperViewModel { Input = "Ab" };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsNato = true;

        Assert.Equal(SpellingAlphabetKind.Nato, vm.Alphabet);
        Assert.Equal("Alfa – Bravo", vm.Spelled);
        Assert.Equal("Alfa", vm.TableRows[0].Word);
        Assert.Contains(nameof(SpellingHelperViewModel.Spelled), changed);
        Assert.Contains(nameof(SpellingHelperViewModel.IsTraditional), changed);
    }

    [Fact]
    public void Unticking_a_radio_button_does_not_change_the_alphabet()
    {
        var vm = new SpellingHelperViewModel();

        vm.IsTraditional = false;

        Assert.Equal(SpellingAlphabetKind.Traditional, vm.Alphabet);
    }

    [Fact]
    public void A_text_handed_over_is_capped_at_the_input_length()
    {
        var vm = new SpellingHelperViewModel();

        vm.SpellCommand.Execute(new string('a', SpellingHelperViewModel.MaxInputLength + 50));

        Assert.Equal(SpellingHelperViewModel.MaxInputLength, vm.Input?.Length);
    }
}
