using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

/// <summary>Hovering a TMO or DMO Funkgruppe on the Führung board lights up every other card that has it.</summary>
public sealed class LeadershipFunkgruppeHighlightTests
{
    private static LocalIncidentSession TwoAbschnitte()
    {
        var session = NewSession();
        session.UpdateLeadershipPost(null, null, null, new Funkgruppen(["FW 1000"], ["307_F"]), null);
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 1000"], ["301_F"]));
        var nord = session.Incident.CommandNodes[^1].Id;
        session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Dach", nord, funkgruppen: new Funkgruppen(null, ["301_F"]));
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Süd", funkgruppen: new Funkgruppen(["FW 2000"], ["307_F"]));
        return session;
    }

    private static BoardNodeCard Card(LeadershipViewModel vm, string name) =>
        vm.Sections.Concat(vm.Sections.SelectMany(s => s.Children)).Single(c => c.Name == name);

    [Fact]
    public void Hovering_a_dmo_lights_up_the_same_dmo_on_every_card_and_nothing_else()
    {
        using var vm = NewVm(TwoAbschnitte());
        var nord = Card(vm, "Nord");

        vm.HighlightFunkgruppe(nord.DmoLines.Single());

        Assert.True(nord.DmoLines.Single().Highlight.IsHighlighted);
        Assert.True(Card(vm, "Dach").DmoLines.Single().Highlight.IsHighlighted);
        Assert.False(vm.El.DmoLines.Single().Highlight.IsHighlighted);
        Assert.False(Card(vm, "Süd").DmoLines.Single().Highlight.IsHighlighted);
        Assert.False(nord.TmoLines.Single().Highlight.IsHighlighted);
    }

    [Fact]
    public void Hovering_the_einsatzleitungs_tmo_lights_up_the_abschnitt_that_shares_it()
    {
        using var vm = NewVm(TwoAbschnitte());

        vm.HighlightFunkgruppe(vm.El.TmoLines.Single());

        Assert.True(vm.El.TmoLines.Single().Highlight.IsHighlighted);
        Assert.True(Card(vm, "Nord").TmoLines.Single().Highlight.IsHighlighted);
        Assert.False(Card(vm, "Süd").TmoLines.Single().Highlight.IsHighlighted);
    }

    [Fact]
    public void A_tmo_and_a_dmo_of_the_same_name_are_not_the_same_funkgruppe()
    {
        var session = NewSession();
        session.UpdateLeadershipPost(null, null, null, new Funkgruppen(["X 1"], ["X 1"]), null);
        using var vm = NewVm(session);

        vm.HighlightFunkgruppe(vm.El.TmoLines.Single());

        Assert.False(vm.El.TmoLines.Single().Highlight.IsHighlighted);
        Assert.False(vm.El.DmoLines.Single().Highlight.IsHighlighted);
    }

    [Fact]
    public void A_funkgruppe_shown_only_once_is_not_highlighted()
    {
        using var vm = NewVm(TwoAbschnitte());
        var sued = Card(vm, "Süd");

        vm.HighlightFunkgruppe(sued.TmoLines.Single());

        Assert.False(sued.TmoLines.Single().Highlight.IsHighlighted);
    }

    [Fact]
    public void Clearing_the_highlight_turns_every_line_off()
    {
        using var vm = NewVm(TwoAbschnitte());
        vm.HighlightFunkgruppe(vm.El.DmoLines.Single());

        vm.ClearFunkgruppeHighlight();

        Assert.False(vm.El.DmoLines.Single().Highlight.IsHighlighted);
        Assert.False(Card(vm, "Süd").DmoLines.Single().Highlight.IsHighlighted);
    }

    [Fact]
    public void A_card_added_by_a_refresh_takes_the_current_highlight()
    {
        var session = NewSession();
        session.UpdateLeadershipPost(null, null, null, new Funkgruppen(["FW 1000"], null), null);
        using var vm = NewVm(session);
        vm.HighlightFunkgruppe(vm.El.TmoLines.Single());
        Assert.False(vm.El.TmoLines.Single().Highlight.IsHighlighted); // alone so far

        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 1000"], null));

        Assert.True(vm.El.TmoLines.Single().Highlight.IsHighlighted);
        Assert.True(Card(vm, "Nord").TmoLines.Single().Highlight.IsHighlighted);
    }

    [Fact]
    public void A_line_from_no_card_on_the_board_highlights_nothing()
    {
        using var vm = NewVm(TwoAbschnitte());

        vm.HighlightFunkgruppe(new FunkgruppeLine("FW 1000", null));

        Assert.False(vm.El.TmoLines.Single().Highlight.IsHighlighted);
    }
}
