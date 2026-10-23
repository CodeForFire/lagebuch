using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class LeadershipBoardCommandTests
{
    private static LocalIncidentSession ReadOnlySession(Action<LocalIncidentSession> seed)
    {
        var store = new FakeStore();
        var seeded = NewSession(store);
        seed(seeded);
        seeded.Close();
        return LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
    }

    private static LeadershipViewModel NewVmWithConfirm(
        IIncidentSession session, List<(string Title, string Message, Action Confirmed, string Label)> asked) =>
        new(session, Clock, MasterDataSet.Empty, () => { }, (title, message, confirmed, label) => asked.Add((title, message, confirmed, label)));

    [Fact]
    public void A_unit_can_be_moved_to_another_node_or_unplaced()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        using var vm = NewVm(session);

        Assert.True(vm.CanMoveUnit(unit, nord));
        Assert.False(vm.CanMoveUnit(unit, null)); // already unplaced
        Assert.True(vm.MoveUnit(unit, nord));
        Assert.True(vm.CanMoveUnit(unit, null));
    }

    [Fact]
    public void A_unit_cannot_be_moved_on_a_read_only_incident()
    {
        Guid nord = default;
        Guid unit = default;
        var session = ReadOnlySession(s =>
        {
            nord = AddNode(s, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
            unit = AddUnit(s, "FF Emmering", null);
        });
        using var vm = NewVm(session);

        Assert.False(vm.CanMoveUnit(unit, nord));
        Assert.False(vm.MoveUnit(unit, nord));
        Assert.Null(vm.ActionError);
    }

    [Fact]
    public void An_unknown_unit_cannot_be_moved()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        using var vm = NewVm(session);

        Assert.False(vm.CanMoveUnit(Guid.NewGuid(), nord));
    }

    [Fact]
    public void A_unit_cannot_be_moved_to_a_partner_a_dissolved_or_an_unknown_node()
    {
        var session = NewSession();
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        var gone = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        session.DissolveCommandNode(gone);
        var unit = AddUnit(session, "FF Aich", null);
        using var vm = NewVm(session);

        Assert.False(vm.CanMoveUnit(unit, rd));
        Assert.False(vm.CanMoveUnit(unit, gone));
        Assert.False(vm.CanMoveUnit(unit, Guid.NewGuid()));
    }

    [Fact]
    public void A_unit_cannot_be_moved_to_the_node_it_is_already_in()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var unit = AddUnit(session, "FF Aich", null, nord);
        using var vm = NewVm(session);

        Assert.False(vm.CanMoveUnit(unit, nord));
    }

    [Fact]
    public void Moving_a_unit_takes_it_off_one_card_and_onto_the_other()
    {
        var session = NewSession();
        var a = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var b = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", a);
        var changes = new int[1];
        using var vm = NewVm(session, changes);

        Assert.True(vm.MoveUnit(unit, b));

        Assert.Equal(1, changes[0]);
        Assert.Empty(vm.Sections.Single(c => c.Id == a).Units);
        Assert.Equal(unit, Assert.Single(vm.Sections.Single(c => c.Id == b).Units).Id);
        Assert.Empty(vm.UnplacedUnits);
        Assert.Null(vm.ActionError);
    }

    [Fact]
    public void A_refusal_from_the_session_becomes_the_action_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var unit = AddUnit(session, "FF Emmering", null);
        var changes = new int[1];
        var received = new SnapshotRoundTrippingSession(session);
        var vm = NewVm(received, changes);
        vm.Dispose(); // the board no longer follows: it still offers the node a peer dissolves next
        session.DissolveCommandNode(nord);

        Assert.True(vm.CanMoveUnit(unit, nord));
        Assert.False(vm.MoveUnit(unit, nord));

        Assert.StartsWith("Nicht möglich: ", vm.ActionError, StringComparison.Ordinal);
        Assert.Equal(0, changes[0]);
        Assert.Null(session.Incident.Forces.Single().NodeId);
    }

    [Fact]
    public void The_next_command_clears_the_action_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var unit = AddUnit(session, "FF Emmering", null);
        var vm = NewVm(new SnapshotRoundTrippingSession(session));
        vm.Dispose();
        session.DissolveCommandNode(nord);
        vm.MoveUnit(unit, nord);
        Assert.NotNull(vm.ActionError);

        vm.MoveUnit(unit, null);

        Assert.Null(vm.ActionError);
    }

    [Fact]
    public void Dragging_a_unit_is_tracked_until_it_ends()
    {
        var session = NewSession();
        var unit = AddUnit(session, "FF Emmering", null);
        using var vm = NewVm(session);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.BeginDrag(unit);
        Assert.True(vm.IsDraggingUnit);
        Assert.Equal(unit, vm.DraggingUnitId);
        Assert.Contains(nameof(LeadershipViewModel.IsDraggingUnit), raised);

        vm.EndDrag();
        Assert.False(vm.IsDraggingUnit);
        Assert.Null(vm.DraggingUnitId);
    }

    [Fact]
    public void Dragging_is_ignored_on_a_read_only_incident()
    {
        Guid unit = default;
        var session = ReadOnlySession(s => unit = AddUnit(s, "FF Emmering", null));
        using var vm = NewVm(session);

        vm.BeginDrag(unit);

        Assert.False(vm.IsDraggingUnit);
    }

    [Fact]
    public void The_dissolve_command_can_execute_exactly_when_the_node_can_be_dissolved()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var voll = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        var unit = AddUnit(session, "FF Aich", null, voll);
        using var vm = NewVm(session);
        var leer = vm.Sections[0];
        var belegt = vm.Sections[1];

        Assert.True(leer.DissolveCommand.CanExecute(null));
        Assert.False(belegt.DissolveCommand.CanExecute(null));

        var raised = 0;
        belegt.DissolveCommand.CanExecuteChanged += (_, _) => raised++;
        session.MoveForceUnit(unit, null);

        Assert.True(belegt.DissolveCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public void Dissolving_asks_first_with_the_exact_wording_and_acts_only_on_confirm()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var asked = new List<(string Title, string Message, Action Confirmed, string Label)>();
        var changes = new int[1];
        using var vm = new LeadershipViewModel(
            session, Clock, MasterDataSet.Empty, () => changes[0]++, (t, m, c, l) => asked.Add((t, m, c, l)));

        vm.Sections[0].DissolveCommand.Execute(null);

        var (title, message, confirmed, label) = Assert.Single(asked);
        Assert.Equal("Abschnitt auflösen?", title);
        Assert.Equal("„Abschnitt 1 Nord“ wird aufgelöst. Das Einsatztagebuch behält den Eintrag.", message);
        Assert.Equal("AUFLÖSEN", label);
        Assert.True(session.Incident.CommandNodes.Single(n => n.Id == nord).IsLive);
        Assert.Equal(0, changes[0]);

        confirmed();

        Assert.False(session.Incident.CommandNodes.Single(n => n.Id == nord).IsLive);
        Assert.Equal(1, changes[0]);
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void Dissolving_without_a_confirm_callback_runs_directly()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz");
        using var vm = NewVm(session);

        vm.Sections[0].DissolveCommand.Execute(null);

        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void A_dissolve_the_session_refuses_becomes_the_action_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var asked = new List<(string Title, string Message, Action Confirmed, string Label)>();
        using var vm = NewVmWithConfirm(session, asked);
        vm.Sections[0].DissolveCommand.Execute(null);
        session.DissolveCommandNode(nord); // a peer was quicker

        asked.Single().Confirmed();

        Assert.StartsWith("Nicht möglich: ", vm.ActionError, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_can_be_dissolved_through_the_command_on_a_read_only_incident()
    {
        var session = ReadOnlySession(s => AddNode(s, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord"));
        using var vm = NewVm(session);

        Assert.False(vm.Sections[0].DissolveCommand.CanExecute(null));
    }

    [Fact]
    public void The_Stufe_choices_list_A_to_D_and_none_with_the_current_one_marked()
    {
        var session = NewSession();
        session.SetFuehrungsstufe(Fuehrungsstufe.B);
        using var vm = NewVm(session);

        var choices = vm.El.StufeChoices;

        Assert.Equal(["Stufe A", "Stufe B", "Stufe C", "Stufe D", "Keine"], choices.Select(c => c.Label));
        Assert.Equal(Fuehrungsstufe.B, Assert.Single(choices, c => c.IsCurrent).Stufe);
    }

    [Fact]
    public void With_no_Stufe_set_none_is_the_current_choice()
    {
        using var vm = NewVm(NewSession());

        var current = Assert.Single(vm.El.StufeChoices, c => c.IsCurrent);

        Assert.Null(current.Stufe);
        Assert.Equal("Keine", current.Label);
    }

    [Fact]
    public void Selecting_a_Stufe_sets_it_and_the_Fuehrungsstufe_entry_appears()
    {
        var session = NewSession();
        var changes = new int[1];
        using var vm = NewVm(session, changes);
        var etbBefore = session.Incident.Journal.Count;

        vm.El.StufeChoices.Single(c => c.Stufe == Fuehrungsstufe.C).SelectCommand.Execute(null);

        Assert.Equal(Fuehrungsstufe.C, session.Incident.Fuehrungsstufe);
        Assert.Equal(1, changes[0]);
        Assert.Equal("Stufe C", vm.El.StufeText);
        Assert.Equal(Fuehrungsstufe.C, Assert.Single(vm.El.StufeChoices, c => c.IsCurrent).Stufe);
        Assert.Equal(etbBefore + 1, session.Incident.Journal.Count);
    }

    [Fact]
    public void Selecting_the_current_Stufe_does_nothing()
    {
        var session = NewSession();
        session.SetFuehrungsstufe(Fuehrungsstufe.B);
        var changes = new int[1];
        using var vm = NewVm(session, changes);
        var etbBefore = session.Incident.Journal.Count;

        vm.El.StufeChoices.Single(c => c.IsCurrent).SelectCommand.Execute(null);

        Assert.Equal(0, changes[0]);
        Assert.Equal(etbBefore, session.Incident.Journal.Count);
    }

    [Fact]
    public void Selecting_Keine_clears_the_Stufe()
    {
        var session = NewSession();
        session.SetFuehrungsstufe(Fuehrungsstufe.B);
        using var vm = NewVm(session);

        vm.El.StufeChoices.Single(c => c.Stufe is null).SelectCommand.Execute(null);

        Assert.Null(session.Incident.Fuehrungsstufe);
        Assert.Equal(ElCard.NoStufeText, vm.El.StufeText);
    }

    [Fact]
    public void Selecting_a_Stufe_on_a_read_only_incident_does_nothing()
    {
        var session = ReadOnlySession(_ => { });
        var changes = new int[1];
        using var vm = NewVm(session, changes);

        vm.El.StufeChoices.Single(c => c.Stufe == Fuehrungsstufe.D).SelectCommand.Execute(null);

        Assert.Null(session.Incident.Fuehrungsstufe);
        Assert.Equal(0, changes[0]);
        Assert.Null(vm.ActionError);
    }

    [Fact]
    public void An_unchanged_refresh_keeps_the_Stufe_choices_instance()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        var before = vm.El.StufeChoices;
        var raised = 0;
        vm.El.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(ElCard.StufeChoices) ? 1 : 0;

        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord"); // refreshes the board, not the Stufe

        Assert.Same(before, vm.El.StufeChoices);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void An_Unterabschnitt_a_hostile_snapshot_turns_top_level_moves_out_of_its_parents_children()
    {
        var inner = NewSession();
        var abschnitt = AddNode(inner, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var dach = AddNode(inner, CommandNodeKind.Unterabschnitt, "Dach", abschnitt);
        var parent = CommandNode.Rehydrate(abschnitt, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord", null, null, null, null, null, null, Clock.Now);
        var orphan = CommandNode.Rehydrate(dach, CommandNodeKind.Unterabschnitt, "Dach", abschnitt, null, null, null, null, null, null);
        var hostile = Hostile([parent, orphan]).Incident;
        var hostileNow = false;
        var session = new SnapshotRoundTrippingSession(inner, incident => hostileNow ? hostile : incident);
        using var vm = NewVm(session);
        Assert.Equal(dach, Assert.Single(Assert.Single(vm.Sections).Children).Id);

        hostileNow = true;
        AddNode(inner, CommandNodeKind.Partner, "Polizei"); // any change makes the "host" send the next snapshot

        var card = Assert.Single(vm.Sections);
        Assert.Equal(dach, card.Id);
        Assert.Empty(card.Children);
        Assert.Single(vm.Sections, c => c.Id == dach);
    }

    [Fact]
    public void A_refused_picker_move_on_the_Kraefte_list_surfaces_as_its_action_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddUnit(session, "FF Emmering", null);
        var forces = new ForcesViewModel(session, Clock, MasterDataSet.Empty, () => { });
        var pick = forces.Forces.Single().NodeChoices.Single(c => c.NodeId == nord);
        forces.Dispose();
        session.DissolveCommandNode(nord);

        pick.SelectCommand.Execute(null);

        Assert.StartsWith("Nicht möglich: ", forces.ActionError, StringComparison.Ordinal);
    }

    [Fact]
    public void The_workspace_confirm_dialog_says_AUFLOESEN_for_a_dissolve_and_ENTFERNEN_by_default()
    {
        var clock = new FixedClock(Clock.Now);
        var session = TestSession.StartNew(
            new FakeStore(), clock, Op, IncidentPath, Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddUnit(session, "FF Aich", null);
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty,
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            new TestPdfExporter(),
            null);

        vm.Leadership.Sections.Single(c => c.Id == nord).DissolveCommand.Execute(null);
        Assert.Equal("AUFLÖSEN", vm.PendingConfirm?.ConfirmLabel);
        vm.PendingConfirm?.CancelCommand.Execute(null);

        vm.Forces.Forces.Single().RemoveCommand.Execute(null);
        Assert.Equal("ENTFERNEN", vm.PendingConfirm?.ConfirmLabel);
    }
}
