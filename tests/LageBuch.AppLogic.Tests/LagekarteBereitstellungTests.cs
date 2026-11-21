using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

/// <summary>The Bereitstellungsräume of the Organisation on the Lagekarte (#966).</summary>
public class LagekarteBereitstellungTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Parkplatz = new(48.1785, 11.2555);
    private static readonly GeoPoint Festwiese = new(48.17633, 11.25881);

    [Fact]
    public void The_live_Bereitstellungsraeume_of_the_Organisation_are_listed_and_nothing_else()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord", standort: "Parkplatz Feuerwache");
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1");
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Süd");
        session.DissolveCommandNode(NodeId(session, "BR Süd"));

        var row = Assert.Single(vm.Bereitstellungen);
        Assert.Equal("BR Nord", row.Title);
        Assert.Equal("Parkplatz Feuerwache", row.Detail);
        Assert.True(row.HasDetail);
        Assert.False(row.IsPlaced);
        Assert.Equal("BR Nord auf Karte setzen", row.PlaceLabel);
        Assert.False(vm.HasNoBereitstellungen);
    }

    [Fact]
    public void A_Bereitstellungsraum_picked_from_the_list_is_placed_by_the_next_tap()
    {
        var (session, vm, changes, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");

        vm.PlaceBereitstellungCommand.Execute(vm.Bereitstellungen[0]);
        Assert.Equal("Auf die Karte tippen: BR Nord platzieren", vm.PlacingHint);
        vm.MapTappedCommand.Execute(Parkplatz);

        Assert.Equal(Parkplatz, session.Incident.CommandNodes.Single().Position);
        var marker = Assert.Single(vm.Markers);
        Assert.True(marker.IsBereitstellung);
        Assert.False(marker.IsEinsatzort);
        Assert.Equal("BR Nord", marker.Title);
        Assert.Equal("Bereitstellungsraum BR Nord: Standort", marker.AutomationName);
        Assert.Same(marker, Assert.Single(vm.Signs));
        Assert.Equal("Bereitstellungsraum BR Nord: taktisches Zeichen", marker.SignAutomationName);
        Assert.True(vm.Bereitstellungen.Single().IsPlaced);
        Assert.Equal("BR Nord versetzen", vm.Bereitstellungen.Single().PlaceLabel);
        Assert.Equal(1, changes());
    }

    [Fact]
    public void A_rename_in_the_Organisation_renames_the_mark_on_the_map()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        var id = NodeId(session, "BR Nord");
        session.PlaceCommandNode(id, Parkplatz);

        session.UpdateCommandNode(id, "BR Feuerwache", null, "Hof", null, null, null);

        Assert.Equal("BR Feuerwache", Assert.Single(vm.Markers).Title);
        Assert.Equal("BR Feuerwache", Assert.Single(vm.Bereitstellungen).Title);
        Assert.Equal("Hof", vm.Bereitstellungen[0].Detail);
        Assert.Equal(Parkplatz, vm.Markers[0].Position);
    }

    [Fact]
    public void A_dissolved_Bereitstellungsraum_leaves_the_map()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        var id = NodeId(session, "BR Nord");
        session.PlaceCommandNode(id, Parkplatz);

        session.DissolveCommandNode(id);

        Assert.Empty(vm.Markers);
        Assert.Empty(vm.Signs);
        Assert.Empty(vm.Bereitstellungen);
        Assert.True(vm.HasNoBereitstellungen);
    }

    [Fact]
    public void A_pick_whose_Bereitstellungsraum_is_dissolved_meanwhile_is_dropped()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Süd");
        vm.PlaceBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        session.DissolveCommandNode(NodeId(session, "BR Nord"));

        Assert.False(vm.IsPlacing);
    }

    [Fact]
    public void Taking_a_Bereitstellungsraum_off_the_map_asks_first()
    {
        var confirms = new List<(string Title, string Message, Action Confirmed)>();
        var (session, vm, _, _) = Open((t, m, a) => confirms.Add((t, m, a)));
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.PlaceCommandNode(NodeId(session, "BR Nord"), Parkplatz);

        vm.RemoveBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        var (title, message, confirmed) = Assert.Single(confirms);
        Assert.Equal("Bereitstellungsraum von der Karte nehmen?", title);
        Assert.Equal("„BR Nord“ von der Lagekarte nehmen? In der Organisation bleibt er bestehen.", message);
        Assert.Equal(Parkplatz, session.Incident.CommandNodes.Single().Position);

        confirmed();

        Assert.Null(session.Incident.CommandNodes.Single().Position);
        Assert.Empty(vm.Markers);
        Assert.Equal("Bereitstellungsraum BR Nord von der Lagekarte genommen", session.Incident.Journal[^1].Text);
    }

    [Fact]
    public void A_dragged_Bereitstellungsraum_moves_and_its_sign_moves_alone()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.PlaceCommandNode(NodeId(session, "BR Nord"), Parkplatz);

        vm.MoveMarker(vm.Markers.Single(), Festwiese);
        vm.MoveLabel(vm.Signs.Single(), new LabelOffset(-80, 30));

        var node = session.Incident.CommandNodes.Single();
        Assert.Equal(Festwiese, node.Position);
        Assert.Equal(new LabelOffset(-80, 30), node.LabelOffset);
        Assert.Equal(new LabelOffset(-80, 30), vm.Signs.Single().LabelOffset);
    }

    [Fact]
    public void A_tap_on_a_Bereitstellungsraum_opens_its_details_from_the_Organisation()
    {
        var (session, vm, _, edited) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        var id = NodeId(session, "BR Nord");
        session.PlaceCommandNode(id, Parkplatz);

        vm.BeginMarkerDrag(vm.Markers.Single(), (20, 10), (20, 10));
        Assert.True(vm.EndMarkerDrag((21, 11), ToGeo));
        vm.BeginSignDrag(vm.Signs.Single(), (50, 50), new LabelOffset(30, 30));
        Assert.True(vm.EndSignDrag((50, 50), ToGeo));
        vm.EditBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        Assert.Equal([id, id, id], edited);
        Assert.Equal(Parkplatz, session.Incident.CommandNodes.Single().Position);
    }

    [Fact]
    public void A_tap_on_a_Bereitstellungsraum_while_placing_places_and_opens_nothing()
    {
        var (session, vm, _, edited) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.PlaceCommandNode(NodeId(session, "BR Nord"), Parkplatz);
        vm.PlaceEinsatzortCommand.Execute(null);

        vm.BeginMarkerDrag(vm.Markers.Single(m => m.IsBereitstellung), (20, 10), (20, 10));
        vm.EndMarkerDrag((20, 10), ToGeo);

        Assert.Empty(edited);
        Assert.Equal(new GeoPoint(10, 20), session.Incident.Location);
    }

    [Fact]
    public void A_closed_Einsatz_lists_its_Bereitstellungsraeume_but_takes_no_marks()
    {
        var (session, _, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.PlaceCommandNode(NodeId(session, "BR Nord"), Parkplatz);
        session.Close();
        using var closed = new LagekarteViewModel(session, () => { });

        closed.PlaceBereitstellungCommand.Execute(closed.Bereitstellungen[0]);
        closed.RemoveBereitstellungCommand.Execute(closed.Bereitstellungen[0]);

        Assert.False(closed.IsPlacing);
        Assert.Null(closed.ActionError);
        Assert.Single(closed.Markers);
        Assert.Equal(Parkplatz, session.Incident.CommandNodes.Single().Position);
    }

    [Fact]
    public void A_pick_survives_a_change_while_its_Bereitstellungsraum_is_still_there()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        vm.PlaceBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Süd");

        Assert.True(vm.IsPlacing);
    }

    [Fact]
    public void The_empty_hint_follows_the_Organisation()
    {
        var (session, vm, _, _) = Open();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.True(vm.HasNoBereitstellungen);

        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");

        Assert.False(vm.HasNoBereitstellungen);
        Assert.Contains(nameof(LagekarteViewModel.HasNoBereitstellungen), raised);
        Assert.False(vm.Bereitstellungen[0].HasDetail);
    }

    [Fact]
    public void Removing_one_not_on_the_map_asks_nothing()
    {
        var confirms = 0;
        var (session, vm, _, _) = Open((_, _, _) => confirms++);
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");

        vm.RemoveBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        Assert.Equal(0, confirms);
    }

    [Fact]
    public void Without_a_confirm_dialog_removing_takes_it_off_at_once()
    {
        var (session, _, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.PlaceCommandNode(NodeId(session, "BR Nord"), Parkplatz);
        using var vm = new LagekarteViewModel(session, () => { });

        vm.RemoveBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        Assert.Null(session.Incident.CommandNodes.Single().Position);
        Assert.Empty(vm.Markers);
    }

    [Fact]
    public void Showing_a_placed_Bereitstellungsraum_centres_the_map_on_it()
    {
        var (session, vm, _, _) = Open();
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "BR Nord");
        session.PlaceCommandNode(NodeId(session, "BR Nord"), Festwiese);

        vm.CenterOnBereitstellungCommand.Execute(vm.Bereitstellungen[0]);

        Assert.Equal(Festwiese, vm.Map.Center);
    }

    [Fact]
    public void A_tap_on_a_unit_s_sign_while_placing_still_places_at_the_hand()
    {
        var (session, vm, _, edited) = Open();
        session.AddForceUnit("FF Musterstadt", 6, "LF 10/1");
        session.PlaceForceUnit(session.Incident.Forces[0].Id, Parkplatz);
        vm.PlaceEinsatzortCommand.Execute(null);

        vm.BeginSignDrag(vm.Signs.Single(), (50, 40), new LabelOffset(30, 30));
        vm.EndSignDrag((50, 40), ToGeo);

        Assert.Equal(new GeoPoint(40, 50), session.Incident.Location);
        Assert.Empty(edited);
    }

    private static GeoPoint ToGeo(double x, double y) => new(y, x);

    private static Guid NodeId(LocalIncidentSession session, string name) =>
        session.Incident.CommandNodes.Single(n => n.Name == name).Id;

    private static (LocalIncidentSession Session, LagekarteViewModel Vm, Func<int> Changes, List<Guid> Edited) Open(
        Action<string, string, Action>? requestConfirm = null)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var changes = 0;
        var edited = new List<Guid>();
        var vm = new LagekarteViewModel(
            session, () => changes++, requestConfirm: requestConfirm ?? ((_, _, confirmed) => confirmed()), editCommandNode: edited.Add);
        return (session, vm, () => changes, edited);
    }
}
