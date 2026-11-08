using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class LagefuehrungBoardTests
{
    private static MasterDataSet WithTalkGroups() =>
        MasterDataSet.Empty with
        {
            TalkGroups =
            [
                new TalkGroup("WR_FFB", null, TalkGroupKind.Tmo),
                new TalkGroup("FW_FFB", "1000", TalkGroupKind.Tmo, IsFavorite: true),
                new TalkGroup("301_F", null, TalkGroupKind.Dmo),
            ],
        };

    [Fact]
    public void A_lone_Feuerwehr_shows_no_Lagefuehrung_mark()
    {
        using var vm = NewVm(NewSession());

        Assert.Equal(LagefuehrungMark.None, vm.El.Lagefuehrung);
    }

    [Fact]
    public void Beside_a_partner_the_Feuerwehr_holds_the_badge_and_the_partner_offers_to_take_it()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Partner, "Polizei");

        using var vm = NewVm(session);

        Assert.Equal(new LagefuehrungMark(ShowsBadge: true, ShowsTake: false), vm.El.Lagefuehrung);
        Assert.Equal(new LagefuehrungMark(ShowsBadge: false, ShowsTake: true), vm.Partners.Single().Lagefuehrung);
    }

    [Fact]
    public void Taking_the_Lagefuehrung_on_a_partner_card_moves_the_badge_and_back()
    {
        var session = NewSession();
        var polizei = AddNode(session, CommandNodeKind.Partner, "Polizei");
        AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        var changes = new int[1];
        using var vm = NewVm(session, changes);
        var polizeiCard = vm.Partners.Single(p => p.Id == polizei);
        var rdCard = vm.Partners.Single(p => p.Id != polizei);

        polizeiCard.TakeLagefuehrungCommand.Execute(null);

        Assert.Equal(polizei, session.Incident.Lagefuehrung?.Id);
        Assert.True(polizeiCard.Lagefuehrung.ShowsBadge);
        Assert.False(rdCard.Lagefuehrung.ShowsBadge);
        Assert.Equal(new LagefuehrungMark(ShowsBadge: false, ShowsTake: true), vm.El.Lagefuehrung);
        Assert.Equal(1, changes[0]);

        vm.El.TakeLagefuehrungCommand.Execute(null);

        Assert.Null(session.Incident.Lagefuehrung);
        Assert.True(vm.El.Lagefuehrung.ShowsBadge);
        Assert.False(polizeiCard.Lagefuehrung.ShowsBadge);
    }

    [Fact]
    public void A_partner_dissolved_by_a_peer_meanwhile_turns_the_take_into_an_action_error()
    {
        var session = NewSession();
        var polizei = AddNode(session, CommandNodeKind.Partner, "Polizei");
        AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        using var vm = NewVm(session);
        var stale = vm.Partners.Single(p => p.Id == polizei);

        // The command still hangs off the card the user is looking at, as a peer's dissolve races it.
        session.Incident.DissolveCommandNode(Clock, Op, polizei);
        stale.TakeLagefuehrungCommand.Execute(null);

        Assert.Equal("Nicht möglich: Die Lageführung kann nur eine Einsatzleitung haben.", vm.ActionError);
        Assert.Null(session.Incident.Lagefuehrung);
    }

    [Fact]
    public void A_read_only_board_shows_who_holds_the_Lagefuehrung_but_offers_no_take()
    {
        var store = new FakeStore();
        var seed = NewSession(store);
        var polizei = AddNode(seed, CommandNodeKind.Partner, "Polizei");
        seed.SetLagefuehrung(polizei);
        seed.Close();

        using var vm = NewVm(LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath));

        Assert.Equal(new LagefuehrungMark(ShowsBadge: true, ShowsTake: false), vm.Partners.Single().Lagefuehrung);
        Assert.Equal(LagefuehrungMark.None, vm.El.Lagefuehrung);
        Assert.False(vm.Partners.Single().TakeLagefuehrungCommand.CanExecute(null));
        Assert.False(vm.El.TakeLagefuehrungCommand.CanExecute(null));
        Assert.False(vm.EditDispatchChannelCommand.CanExecute(null));
    }

    [Fact]
    public void An_Abschnitt_card_can_never_take_the_Lagefuehrung()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        AddNode(session, CommandNodeKind.Partner, "Polizei");

        using var vm = NewVm(session);

        var nord = vm.Sections.Single();
        Assert.Equal(LagefuehrungMark.None, nord.Lagefuehrung);
        Assert.False(nord.TakeLagefuehrungCommand.CanExecute(null));
    }

    [Fact]
    public void The_badge_reads_the_Leitstelle_and_its_channel_once_one_is_recorded()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        Assert.Equal("ILS", vm.DispatchCentreText);

        session.SetDispatchChannel("TMO WR_FFB");

        Assert.Equal("ILS · TMO WR_FFB", vm.DispatchCentreText);
    }

    [Fact]
    public void The_badge_opens_the_channel_dialog_seeded_with_the_recorded_channel()
    {
        var session = NewSession();
        session.SetDispatchChannel("TMO WR_FFB");
        DispatchChannelDialogViewModel? opened = null;
        using var vm = new LeadershipViewModel(
            session, Clock, WithTalkGroups(), () => { }, openChannelDialog: d => opened = d);

        vm.EditDispatchChannelCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Equal("Kanal zur ILS", opened.Title);
        Assert.Equal("TMO WR_FFB", opened.Channel);
    }

    [Fact]
    public void The_dialog_offers_the_Stammdaten_groups_with_their_kind_favourites_first()
    {
        var dialog = new DispatchChannelDialogViewModel(NewSession(), WithTalkGroups());

        Assert.Equal(["TMO FW_FFB (KW: 1000)", "TMO WR_FFB", "DMO 301_F"], dialog.ChannelOptions);
    }

    [Fact]
    public void Saving_stores_the_channel_in_the_catalogues_spelling_and_closes()
    {
        var session = NewSession();
        var changes = 0;
        var closed = 0;
        var dialog = new DispatchChannelDialogViewModel(session, WithTalkGroups(), () => changes++) { Channel = " tmo wr_ffb " };
        dialog.Closed += (_, _) => closed++;

        dialog.SaveCommand.Execute(null);

        Assert.Equal("TMO WR_FFB", session.Incident.DispatchChannel);
        Assert.Equal((1, 1), (changes, closed));
    }

    [Fact]
    public void Saving_an_unchanged_channel_writes_nothing()
    {
        var session = NewSession();
        session.SetDispatchChannel("Kanal 5");
        var journal = session.Incident.Journal.Count;
        var changes = 0;
        var dialog = new DispatchChannelDialogViewModel(session, MasterDataSet.Empty, () => changes++);

        dialog.SaveCommand.Execute(null);

        Assert.Equal(journal, session.Incident.Journal.Count);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Saving_a_blank_channel_clears_it()
    {
        var session = NewSession();
        session.SetDispatchChannel("TMO WR_FFB");
        var dialog = new DispatchChannelDialogViewModel(session, MasterDataSet.Empty) { Channel = "  " };

        dialog.SaveCommand.Execute(null);

        Assert.Null(session.Incident.DispatchChannel);
    }

    [Fact]
    public void A_channel_over_the_cap_is_refused_with_a_save_error_and_the_dialog_stays_open()
    {
        var session = NewSession();
        var closed = false;
        var dialog = new DispatchChannelDialogViewModel(session, MasterDataSet.Empty)
        {
            Channel = new string('x', CommandNode.MaxTextLength + 1),
        };
        dialog.Closed += (_, _) => closed = true;

        dialog.SaveCommand.Execute(null);

        Assert.NotNull(dialog.SaveError);
        Assert.False(closed);
        Assert.Null(session.Incident.DispatchChannel);
    }

    [Fact]
    public void Cancelling_leaves_the_channel_as_it_was()
    {
        var session = NewSession();
        session.SetDispatchChannel("TMO WR_FFB");
        var closed = false;
        var dialog = new DispatchChannelDialogViewModel(session, MasterDataSet.Empty) { Channel = "DMO 301_F" };
        dialog.Closed += (_, _) => closed = true;

        dialog.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal("TMO WR_FFB", session.Incident.DispatchChannel);
    }
}
