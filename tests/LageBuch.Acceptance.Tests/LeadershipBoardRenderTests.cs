using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Sync;

namespace LageBuch.Acceptance.Tests;

// The FÜHRUNG board: the A3 Führungs-sheet as a screen. Cards per node, the units on them, drag and
// drop between them on the desktop and the shared picker everywhere. Doubles as the PR screenshots.
public class LeadershipBoardRenderTests
{
    private const string IncidentPath = "/x.fwincident";

    private static LocalIncidentSession NewSession(FakeStore store, FixedClock clock) =>
        TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Huber", "Florian Emmering 12/1"),
            IncidentPath,
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static Guid AddNode(
        LocalIncidentSession session, CommandNodeKind kind, string name, Guid? parent = null, string? auftrag = null, string? funkgruppe = null)
    {
        session.AddCommandNode(kind, name, parent, auftrag: auftrag, funkgruppe: funkgruppe);
        return session.Incident.CommandNodes[^1].Id;
    }

    private static Guid AddUnit(
        LocalIncidentSession session, string brigade, string callSign, int personnel, int scba, int officers, Guid? nodeId)
    {
        session.AddForceUnit(brigade, personnel, callSign, scbaCount: scba, officerCount: officers);
        var id = session.Incident.Forces[^1].Id;
        if (nodeId is not null)
        {
            session.MoveForceUnit(id, nodeId);
        }

        return id;
    }

    // A Wohnhausbrand in Emmering as the sheet would carry it an hour in.
    private static void Fill(LocalIncidentSession session)
    {
        session.UpdateLeadershipPost("Feuerwehrhaus Emmering", "Florian Emmering 1", "08141 40123", "TMO FW_EMMERING 1000");
        session.AssignRole("Einsatzleiter", "Max Mustermann", "Florian Emmering 1", slot: RoleSlot.Leiter);
        session.AssignRole("Melder", "Lukas Brandl", slot: RoleSlot.Melder);
        session.AssignRole("S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);

        var markt = AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz", auftrag: "Nachrückende Kräfte sammeln");
        AddUnit(session, "FF Steinhöring", "Florian Steinhöring 40/1", 9, 4, 1, markt);

        var nord = AddNode(
            session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord", auftrag: "Brandbekämpfung Wohnhaus, Menschenrettung", funkgruppe: "DMO 301_F");
        session.AssignRole("Abschnittsleiter", "Hans Huber", "Florian Aich 1", nodeId: nord, slot: RoleSlot.Leiter);
        session.AssignRole("Melder", "Lena Lehner", nodeId: nord, slot: RoleSlot.Melder);
        AddUnit(session, "FF Emmering", "Florian Emmering 40/1", 9, 4, 1, nord);
        AddUnit(session, "FF Aich", "Florian Aich 44/1", 9, 2, 1, nord);
        var dach = AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", nord, auftrag: "Riegelstellung Dachstuhl");
        AddUnit(session, "FF Emmering", "Florian Emmering 30/1", 3, 0, 1, dach);

        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd", auftrag: "Schutz Nachbargebäude", funkgruppe: "DMO 302_F");
        session.AssignRole("Abschnittsleiter", "Josef Bauer", "Florian Aich 2", nodeId: sued, slot: RoleSlot.Leiter);
        AddUnit(session, "FF Aich", "Florian Aich 40/1", 9, 4, 1, sued);

        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst", funkgruppe: "TMO RD_FFB 1001");
        session.AssignRole("Einsatzleiter RD", "Sabine Schmid", "Rotkreuz Fürstenfeldbruck 1", nodeId: rd, slot: RoleSlot.Leiter);
        var polizei = AddNode(session, CommandNodeKind.Partner, "Polizei");
        session.AssignRole("Einsatzleiter Polizei", "Thomas Wagner", nodeId: polizei, slot: RoleSlot.Leiter);
        session.AssignRole("Fachberater THW", "Peter Maier", "Heros Fürstenfeldbruck 10/1", slot: RoleSlot.Fachberater);

        AddUnit(session, "FF Emmering", "Florian Emmering 11/2", 6, 0, 0, null);
        session.AssignRole("ZF", "Maria Musterfrau", section: "Wache Süd");
        session.SetFuehrungsstufe(Fuehrungsstufe.B);
    }

    private sealed record Board(Window Window, LeadershipView View, LocalIncidentSession? Session, LeadershipViewModel Vm);

    // What the FÜHRUNG tab gives a module in a 1400 px window: the rail takes 172 px.
    private const double WorkspaceHostWidth = 1228;

    private static Board Show(bool narrow = false, bool filled = true, double? width = null, Action<LocalIncidentSession>? more = null)
    {
        var clock = new FixedClock();
        var session = NewSession(new FakeStore(), clock);
        if (filled)
        {
            Fill(session);
        }

        more?.Invoke(session);
        return Host(session, clock, narrow, session, width);
    }

    private static Board ShowReadOnly()
    {
        var clock = new FixedClock();
        var store = new FakeStore();
        var seed = NewSession(store, clock);
        Fill(seed);
        seed.Close();
        return Host(LocalIncidentSession.OpenReadOnly(store, clock, IncidentPath), clock, narrow: false, null);
    }

    private static Board Host(IIncidentSession session, FixedClock clock, bool narrow, LocalIncidentSession? writable, double? width = null)
    {
        var workspace = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        workspace.Leadership.IsNarrow = narrow;
        var view = new LeadershipView { DataContext = workspace.Leadership };

        // The tab's own frame: the surface and padding the workspace draws round a module.
        var frame = new Border
        {
            Background = (Avalonia.Media.IBrush)Application.Current!.FindResource("SurfaceBrush")!,
            Padding = new Thickness(narrow ? 12 : 24),
            Child = view,
        };
        var root = new Panel { Name = "WorkspaceRoot", Children = { frame } };
        root.Classes.Set("narrow", narrow);
        var window = new Window { Content = root, Width = width ?? (narrow ? 400 : 1400), Height = narrow ? 2900 : width is null ? 1500 : 2000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new(window, view, writable, workspace.Leadership);
    }

    // A control inside a card template: its name is the template's, not the view's.
    // A press and release in the control's own top level: the window, or a flyout's popup.
    private static void Press(Control control)
    {
        var topLevel = TopLevel.GetTopLevel(control)!;
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;
        topLevel.MouseDown(centre, MouseButton.Left);
        topLevel.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static List<Border> Cards(Visual root) =>
        root.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("board-card") && b.DataContext is BoardNodeCard)
            .ToList();

    private static Border Card(Visual root, string name) =>
        Cards(root).Single(b => b.DataContext is BoardNodeCard { Name: var n } && n == name);

    // A control inside a card, as the pointer would hit it: its own name line.
    private static TextBlock NameOf(Border card) =>
        card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "NodeCardName");

    private static Guid UnitId(Board board, string callSign) =>
        AllUnits(board.Vm).Single(u => u.CallSign == callSign).Id;

    private static IEnumerable<BoardUnitRow> AllUnits(LeadershipViewModel vm) =>
        vm.Sections.SelectMany(c => c.Units.Concat(c.Children.SelectMany(ch => ch.Units))).Concat(vm.UnplacedUnits);

    private static Guid? NodeOfUnit(LocalIncidentSession session, Guid unitId) =>
        session.Incident.Forces.Single(u => u.Id == unitId).NodeId;

    private static Guid NodeId(LocalIncidentSession session, string name) =>
        session.Incident.CommandNodes.Single(n => n.Name == name).Id;

    private static DataTransfer Dragging(Guid id)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(LeadershipView.UnitDragFormat, id.ToString()));
        return transfer;
    }

    private static DragEventArgs Over(DataTransfer transfer, Control target)
    {
        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, target, new Point(4, 4), KeyModifiers.None);
        target.RaiseEvent(over);
        Dispatcher.UIThread.RunJobs();
        return over;
    }

    private static void Drop(DataTransfer transfer, Control target)
    {
        target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, target, new Point(4, 4), KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_board_draws_every_part_of_the_sheet()
    {
        var board = Show();
        var view = board.View;

        var badge = view.GetControl<Border>("DispatchCentreBadge");
        Assert.Equal("ILS", badge.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("Max Mustermann · Florian Emmering 1", Named<TextBlock>(view, "ElLeiterText").Text);
        Assert.Equal("Lukas Brandl", Named<TextBlock>(view, "ElMelderText").Text);
        Assert.False(Named<Grid>(view, "ElFurther").IsVisible);
        Assert.Equal("Stufe B", Named<TextBlock>(view, "StufeText").Text);

        // On the desktop the reason AUFLÖSEN is off is the button's tooltip, not a line on the card.
        Assert.All(
            view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "DissolveBlockedText"),
            t => Assert.False(t.IsVisible));
        Assert.Contains("mismatch", Named<TextBlock>(view, "StufeSuggestionText").Classes);

        // The sections wrap into one row of equal width, the Unterabschnitt inside its Abschnitt.
        var sections = view.GetControl<ItemsControl>("SectionCards").GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("board-card") && b.GetVisualAncestors().OfType<Border>().All(a => !a.Classes.Contains("board-card")))
            .ToList();
        Assert.Equal(
            ["Bereitstellung Marktplatz", "Abschnitt 1 Nord", "Abschnitt 2 Süd"],
            sections.Select(b => ((BoardNodeCard)b.DataContext!).Name));
        Assert.All(sections, s => Assert.True(Math.Abs(s.Bounds.Width - 320) <= 0.5, $"width {s.Bounds.Width}"));
        Assert.All(sections, s => Assert.True(Math.Abs(s.Bounds.Top - sections[0].Bounds.Top) <= 0.5));
        var dach = Card(view, "Dach");
        Assert.Same(Card(view, "Abschnitt 1 Nord"), dach.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("board-card")));

        // The partner Einsatzleitungen and the Fachberater beside the Einsatzleitung.
        Assert.Equal(
            ["Rettungsdienst", "Polizei"],
            Cards(view.GetControl<ItemsControl>("TopRowCards")).Select(b => ((BoardNodeCard)b.DataContext!).Name));
        Assert.True(Named<Border>(view, "FachberaterCard").IsVisible);
        Assert.True(view.GetControl<Border>("UnplacedStrip").IsVisible);
        Assert.Contains(
            "Florian Emmering 11/2",
            view.GetControl<ItemsControl>("UnplacedUnits").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains(
            "Abschnitt (Freitext): Wache Süd",
            view.GetControl<ItemsControl>("FreeTextSectionCards").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

        // Only an Abschnitt offers an Unterabschnitt.
        static bool OffersUnterabschnitt(Border card) => card.GetVisualDescendants().OfType<Button>()
            .Any(b => Equals(b.Content, "+ UNTERABSCHNITT") && b.IsEffectivelyVisible
                && ReferenceEquals(b.GetVisualAncestors().OfType<Border>().First(a => a.Classes.Contains("board-card")), card));
        Assert.True(OffersUnterabschnitt(Card(view, "Abschnitt 1 Nord")));
        Assert.False(OffersUnterabschnitt(Card(view, "Bereitstellung Marktplatz")));
        Assert.False(OffersUnterabschnitt(Card(view, "Polizei")));

        Capture(board.Window, "fuehrung-board.png");
    }

    [AvaloniaFact]
    public void The_top_row_wraps_card_by_card_inside_the_workspace_width()
    {
        var board = Show(width: WorkspaceHostWidth, more: s =>
        {
            s.AddCommandNode(CommandNodeKind.Partner, "THW", funkgruppe: "TMO THW_FFB 1002");
            s.AddCommandNode(CommandNodeKind.Partner, "Wasserwacht");
        });
        var boardRoot = board.View.GetControl<StackPanel>("BoardRoot");
        var row = board.View.GetControl<ItemsControl>("TopRowCards");
        var el = Named<Border>(board.View, "ElCard");
        var partners = Cards(row);
        Assert.Equal(["Rettungsdienst", "Polizei", "THW", "Wasserwacht"], partners.Select(b => ((BoardNodeCard)b.DataContext!).Name));
        var cards = new List<Border> { el };
        cards.AddRange(partners);
        cards.Add(Named<Border>(board.View, "FachberaterCard"));

        double Top(Border b) => b.TranslatePoint(default, boardRoot)!.Value.Y;
        foreach (var card in cards)
        {
            var right = card.TranslatePoint(new Point(card.Bounds.Width, 0), boardRoot)!.Value.X;
            Assert.True(right <= boardRoot.Bounds.Width + 0.5, $"right edge {right} past {boardRoot.Bounds.Width}");
            Assert.True(Math.Abs(card.Bounds.Width - 320) <= 0.5, $"width {card.Bounds.Width}");
        }

        // The sheet's row: Einsatzleitung, Rettungsdienst and Polizei side by side; the rest flows on.
        Assert.True(Math.Abs(Top(partners[0]) - Top(el)) <= 0.5);
        Assert.True(Math.Abs(Top(partners[1]) - Top(el)) <= 0.5);
        Assert.True(Top(partners[2]) > Top(el) + 1);
        Capture(board.Window, "fuehrung-board-workspace.png");
    }

    [AvaloniaFact]
    public void A_unit_title_is_never_trimmed_in_a_card_or_an_unterabschnitt()
    {
        var board = Show(more: s => s.AddForceUnit(
            "FF Fürstenfeldbruck", 9, "Florian Fürstenfeldbruck 40/1", scbaCount: 4, officerCount: 1));
        var id = board.Session!.Incident.Forces[^1].Id;
        board.Session.MoveForceUnit(id, NodeId(board.Session, "Dach"));
        Dispatcher.UIThread.RunJobs();

        var titles = board.View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "UnitTitle").ToList();
        Assert.Contains(titles, t => t.Text == "Florian Fürstenfeldbruck 40/1");
        Assert.All(titles, t =>
        {
            Assert.Equal(Avalonia.Media.TextTrimming.None, t.TextTrimming);
            Assert.True(t.TextLayout.TextLines.Count == 1, $"{t.Text} breaks over {t.TextLayout.TextLines.Count} lines");
            Assert.Equal(t.Text, ToolTip.GetTip(t));
        });
    }

    [AvaloniaFact]
    public void The_units_table_lines_its_columns_up_across_rows()
    {
        var board = Show();
        var nord = Card(board.View, "Abschnitt 1 Nord");

        // The Abschnitt's own rows, not its Unterabschnitt's: each card is its own table.
        var strengths = nord.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is BoardUnitRow && t.Text is "0/1/8/9"
                && ReferenceEquals(t.GetVisualAncestors().OfType<Border>().First(a => a.Classes.Contains("board-card")), nord))
            .ToList();
        Assert.Equal(2, strengths.Count);
        var lefts = strengths.Select(t => t.TranslatePoint(default, nord)!.Value.X).ToList();
        Assert.True(Math.Abs(lefts[0] - lefts[1]) <= 0.5, $"{lefts[0]} vs {lefts[1]}");
    }

    [AvaloniaFact]
    public void The_narrow_board_is_one_full_width_column_without_grips()
    {
        var board = Show(narrow: true);
        var view = board.View;
        var width = view.GetControl<StackPanel>("BoardRoot").Bounds.Width;

        foreach (var card in Cards(view).Where(c => c.DataContext is BoardNodeCard { Kind: not CommandNodeKind.Unterabschnitt }))
        {
            Assert.True(Math.Abs(card.Bounds.Width - width) <= 1, $"{((BoardNodeCard)card.DataContext!).Name}: {card.Bounds.Width} of {width}");
        }

        Assert.True(Math.Abs(Named<Border>(view, "ElCard").Bounds.Width - width) <= 1);
        Assert.True(Math.Abs(Named<Border>(view, "FachberaterCard").Bounds.Width - width) <= 1);
        var grips = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("unit-grip")).ToList();
        Assert.NotEmpty(grips);
        Assert.All(grips, g => Assert.False(g.IsVisible));
        Assert.All(
            view.GetVisualDescendants().OfType<NodeChoicePicker>().ToList(),
            p => Assert.True(p.IsEffectivelyVisible && p.IsEffectivelyEnabled));

        // A phone has no hover: the reason AUFLÖSEN is off stands on the card, only where it is off.
        TextBlock Reason(string name) => Card(view, name).GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "DissolveBlockedText");
        Assert.True(Reason("Abschnitt 2 Süd").IsVisible);
        Assert.Equal("Erst die Einheiten verschieben.", Reason("Abschnitt 2 Süd").Text);
        Capture(board.Window, "fuehrung-board-narrow.png");
    }

    [AvaloniaFact]
    public void The_narrow_board_shows_no_reason_on_a_card_that_can_be_dissolved()
    {
        var board = Show(narrow: true, filled: false, more: s => s.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1"));

        var reason = Card(board.View, "Abschnitt 1").GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "DissolveBlockedText");
        Assert.False(reason.IsVisible);
    }

    [AvaloniaFact]
    public void A_further_leiter_typed_on_funktionen_stands_under_weitere_on_the_einsatzleitung()
    {
        var board = Show(more: s => s.AssignRole("Einsatzabschnittsleiter", "Georg Lechner", slot: RoleSlot.Leiter));

        var further = Named<Grid>(board.View, "ElFurther");
        Assert.True(further.IsVisible);
        Assert.Contains("Georg Lechner", further.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal("Max Mustermann · Florian Emmering 1", Named<TextBlock>(board.View, "ElLeiterText").Text);
    }

    [AvaloniaFact]
    public void A_new_incident_shows_only_the_einsatzleitung_and_the_add_buttons()
    {
        var board = Show(filled: false);
        var view = board.View;

        Assert.Empty(Cards(view));
        Assert.True(Named<Border>(view, "ElCard").IsEffectivelyVisible);
        Assert.Equal("—", Named<TextBlock>(view, "ElLeiterText").Text);
        Assert.False(view.GetControl<ItemsControl>("SectionCards").IsVisible);
        Assert.False(view.GetControl<Border>("UnplacedStrip").IsVisible);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(), b => b.Name == "FachberaterCard");
        Assert.False(view.GetControl<ItemsControl>("FreeTextSectionCards").IsVisible);
        Assert.True(view.GetControl<Button>("LeadershipAddAbschnittButton").IsEffectivelyEnabled);
        Assert.True(view.GetControl<Button>("AddRettungsdienstButton").IsEffectivelyEnabled);
        Capture(board.Window, "fuehrung-board-empty.png");
    }

    [AvaloniaFact]
    public void Dropping_a_unit_on_a_card_moves_it_there()
    {
        var board = Show();
        var session = board.Session!;
        var unit = UnitId(board, "Florian Steinhöring 40/1");
        var sued = Card(board.View, "Abschnitt 2 Süd");
        var target = NameOf(sued);
        using var transfer = Dragging(unit);
        board.Vm.BeginDrag(unit);
        Dispatcher.UIThread.RunJobs();

        var over = Over(transfer, target);

        Assert.Equal(DragDropEffects.Move, over.DragEffects);
        Assert.Contains("drop-target", sued.Classes);
        Capture(board.Window, "fuehrung-board-drag.png");

        Drop(transfer, target);
        board.Vm.EndDrag();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NodeId(session, "Abschnitt 2 Süd"), NodeOfUnit(session, unit));
        Assert.DoesNotContain(board.View.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("drop-target"));
        Assert.Contains(board.Vm.Sections.Single(c => c.Name == "Abschnitt 2 Süd").Units, u => u.Id == unit);
    }

    [AvaloniaFact]
    public void A_drop_on_an_unterabschnitt_goes_to_the_unterabschnitt_not_its_abschnitt()
    {
        var board = Show();
        var session = board.Session!;
        var unit = UnitId(board, "Florian Aich 40/1");
        var dach = Card(board.View, "Dach");
        using var transfer = Dragging(unit);

        var over = Over(transfer, NameOf(dach));
        Assert.Equal(DragDropEffects.Move, over.DragEffects);
        Assert.Contains("drop-target", dach.Classes);
        Assert.DoesNotContain("drop-target", Card(board.View, "Abschnitt 1 Nord").Classes);
        Drop(transfer, NameOf(dach));

        Assert.Equal(NodeId(session, "Dach"), NodeOfUnit(session, unit));
    }

    [AvaloniaFact]
    public void A_drop_on_the_strip_takes_the_unit_out_of_its_abschnitt()
    {
        var board = Show();
        var session = board.Session!;
        var unit = UnitId(board, "Florian Emmering 40/1");
        var strip = board.View.GetControl<Border>("UnplacedStrip");
        using var transfer = Dragging(unit);

        var over = Over(transfer, strip);
        Assert.Equal(DragDropEffects.Move, over.DragEffects);
        Assert.Contains("drop-target", strip.Classes);
        Drop(transfer, strip);

        Assert.Null(NodeOfUnit(session, unit));
        Assert.Contains(board.Vm.UnplacedUnits, u => u.Id == unit);
    }

    [AvaloniaFact]
    public void A_partner_card_refuses_a_unit_and_nothing_moves()
    {
        var board = Show();
        var session = board.Session!;
        var unit = UnitId(board, "Florian Aich 40/1");
        var polizei = Card(board.View, "Polizei");
        using var transfer = Dragging(unit);

        var over = Over(transfer, NameOf(polizei));
        Drop(transfer, NameOf(polizei));

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.DoesNotContain("drop-target", polizei.Classes);
        Assert.Equal(NodeId(session, "Abschnitt 2 Süd"), NodeOfUnit(session, unit));
    }

    [AvaloniaFact]
    public void A_unit_dropped_on_its_own_card_is_refused()
    {
        var board = Show();
        var session = board.Session!;
        var unit = UnitId(board, "Florian Aich 40/1");
        using var transfer = Dragging(unit);

        var over = Over(transfer, NameOf(Card(board.View, "Abschnitt 2 Süd")));

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.Equal(NodeId(session, "Abschnitt 2 Süd"), NodeOfUnit(session, unit));
    }

    [AvaloniaFact]
    public void The_picker_on_a_card_row_moves_the_unit()
    {
        var board = Show();
        var session = board.Session!;
        var unit = UnitId(board, "Florian Steinhöring 40/1");
        var picker = Card(board.View, "Bereitstellung Marktplatz").GetVisualDescendants().OfType<NodeChoicePicker>()
            .Single(p => p.DataContext is BoardUnitRow row && row.Id == unit);
        var button = picker.GetControl<Button>("PickerButton");
        Assert.Equal("Bereitstellung Marktplatz", picker.Label);

        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var content = ((Flyout)button.Flyout).Content as Control;
        var pick = content!.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is NodeChoice { Label: "Abschnitt 1 Nord / Dach" });
        pick.Command!.Execute(null);
        button.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NodeId(session, "Dach"), NodeOfUnit(session, unit));
        Assert.Contains(board.Vm.Sections.Single(c => c.Name == "Abschnitt 1 Nord").Children.Single().Units, u => u.Id == unit);
    }

    [AvaloniaFact]
    public void A_read_only_incident_disables_every_board_button_and_refuses_a_drop()
    {
        var board = ShowReadOnly();
        var view = board.View;
        Assert.True(board.Vm.IsReadOnly);

        // The board's own buttons; the scroll bar still scrolls a closed Einsatz.
        var buttons = view.GetControl<StackPanel>("BoardRoot").GetVisualDescendants().OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.False(b.IsEffectivelyEnabled, $"{b.Name} {b.Content} is enabled"));
        Assert.All(
            view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("unit-grip")).ToList(),
            g => Assert.False(g.IsVisible));

        var unitRow = board.Vm.Sections.Single(c => c.Name == "Abschnitt 2 Süd").Units.Single();
        var before = unitRow.NodeLabel;
        using var transfer = Dragging(unitRow.Id);
        var target = NameOf(Card(view, "Bereitstellung Marktplatz"));
        var over = Over(transfer, target);
        Drop(transfer, target);

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.Equal(before, board.Vm.Sections.Single(c => c.Name == "Abschnitt 2 Süd").Units.Single().NodeLabel);
    }

    // Real presses, not Command.Execute plus a raised Click: the flyout used to close in the Click
    // event, which detached the entries before Button ran their command, so a click chose nothing.
    [AvaloniaFact]
    public void Picking_a_fuehrungsstufe_sets_it_and_closes_the_flyout()
    {
        var board = Show();
        var stufe = Named<Button>(board.View, "StufeButton");

        Press(stufe);
        Assert.True(stufe.Flyout!.IsOpen);
        var content = ((Flyout)stufe.Flyout).Content as Control;
        var pick = content!.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is StufeChoice { Label: "Stufe D" });
        Press(pick);

        Assert.Equal(Fuehrungsstufe.D, board.Session!.Incident.Fuehrungsstufe);
        Assert.Equal("Stufe D", Named<TextBlock>(board.View, "StufeText").Text);
        Assert.DoesNotContain("mismatch", Named<TextBlock>(board.View, "StufeSuggestionText").Classes);
        Assert.False(((Flyout)stufe.Flyout).IsOpen);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (dir is null)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.Save(Path.Join(dir, name), PngBitmapEncoderOptions.Default);
    }
}
