using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The FÜHRUNG board: the incident's Führungsorganisation as a live Führungs-sheet. The Einsatzleitung
/// Feuerwehr on top with the partner Einsatzleitungen and Fachberater, below it a card per
/// Bereitstellung and Abschnitt (Unterabschnitte nested), and the units nobody has placed yet.
/// </summary>
/// <remarks>
/// Everything shown is derived by <see cref="ReportingLine"/> and
/// <see cref="FuehrungsspanneHints"/>; this view model never decides placement itself. Each change
/// re-reads <see cref="IIncidentSession.Incident"/> (a joined device rebuilds the aggregate per
/// snapshot) and reconciles every collection by id, so the cards stay put under the user (#294).
/// </remarks>
public sealed partial class LeadershipViewModel : ObservableObject, INarrowAware, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly Action<string, string, Action, string>? _requestConfirm;
    private readonly MasterDataSet _masterData;
    private readonly Action<CommandNodeDialogViewModel>? _openDialog;
    private readonly Action<LeadershipPersonDialogViewModel>? _openPersonDialog;
    private readonly Action<RoleAssignmentDialogViewModel>? _openAssignmentDialog;
    private readonly Action<string, Action> _offerUndo;
    private readonly IClock _clock;
    private readonly FachberaterGroup _fachberaterGroup;

    // The incident as the last refresh showed it: the move guard judges against what the board offers.
    private Incident _incident;

    // The unplaced units whose status says Bereitstellungsraum, as the last refresh found them. They
    // stay unplaced; the board only shows them in every Bereitstellungsraum card (#825).
    private IReadOnlyList<ForceUnit> _waiting = [];

    // Each live node's Funkgruppen against the level above (#878), as the last refresh found them.
    private Dictionary<Guid, FunkCheckResult> _funkCheck = [];

    /// <summary>
    /// <paramref name="requestConfirm"/> asks the host to confirm a destructive action (title, message,
    /// the action, the confirm button's label); without it the action runs at once, as in tests.
    /// <paramref name="openDialog"/> hands the host the node dialog to show; without it the board's
    /// add and edit commands do nothing. <paramref name="openPersonDialog"/> does the same for the
    /// person dialog behind Leiter, Melder, Führungsunterstützung and Fachberater, and
    /// <paramref name="openAssignmentDialog"/> for the dialog that corrects or hands over a
    /// Führungsunterstützung already held.
    /// </summary>
    public LeadershipViewModel(
        IIncidentSession session,
        IClock clock,
        MasterDataSet masterData,
        Action onChanged,
        Action<string, string, Action, string>? requestConfirm = null,
        Action<CommandNodeDialogViewModel>? openDialog = null,
        Action<LeadershipPersonDialogViewModel>? openPersonDialog = null,
        Action<RoleAssignmentDialogViewModel>? openAssignmentDialog = null,
        Action<string, Action>? offerUndo = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        _requestConfirm = requestConfirm;
        _masterData = masterData;
        _openDialog = openDialog;
        _openPersonDialog = openPersonDialog;
        _openAssignmentDialog = openAssignmentDialog;
        _offerUndo = offerUndo ?? ((_, _) => { });
        _clock = clock;
        _incident = session.Incident;
        IsReadOnly = session.IsReadOnly;
        El = new ElCard(EditPost, !IsReadOnly, slot => OpenPerson(null, slot), OpenAssignment);
        DispatchCentreName = masterData.Settings.DispatchCentreName;
        _fachberaterGroup = new FachberaterGroup(Fachberater);
        Refresh();
        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    [ObservableProperty]
    private bool _isNarrow;

    /// <summary>
    /// Why the last board command was refused, in German; null when it went through. Cleared by the
    /// next command. The board itself is left as the next refresh shows it.
    /// </summary>
    [ObservableProperty]
    private string? _actionError;

    /// <summary>The unit being dragged, or null; the view highlights the places it may drop.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraggingUnit))]
    private Guid? _draggingUnitId;

    public bool IsDraggingUnit => DraggingUnitId is not null;

    /// <summary>The node whose card is being dragged, so the view can show where it may land; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraggingNode))]
    private Guid? _draggingNodeId;

    public bool IsDraggingNode => DraggingNodeId is not null;

    /// <summary>What + RETTUNGSDIENST says it does, or why it is off.</summary>
    [ObservableProperty]
    private string _rettungsdienstTip = string.Empty;

    /// <summary>What + POLIZEI says it does, or why it is off.</summary>
    [ObservableProperty]
    private string _polizeiTip = string.Empty;

    /// <summary>The Leitstelle the Einsatzleitung reports to, from the Stammdaten (default "ILS").</summary>
    public string DispatchCentreName { get; }

    public ElCard El { get; }

    /// <summary>Bereitstellungen, Abschnitte and orphaned Unterabschnitte, in reporting-line order.</summary>
    public ObservableCollection<BoardNodeCard> Sections { get; } = [];

    /// <summary>The Einsatzleitungen of partner organisations (Rettungsdienst, Polizei, ...).</summary>
    public ObservableCollection<BoardNodeCard> Partners { get; } = [];

    public ObservableCollection<BoardPersonRow> Fachberater { get; } = [];

    /// <summary>
    /// The sheet's top row as one list, so it wraps card by card: <see cref="El"/>, each of
    /// <see cref="Partners"/> in order, then a <see cref="FachberaterGroup"/> while there are Fachberater.
    /// The same instances throughout; a refresh moves, adds or removes them in place.
    /// </summary>
    public ObservableCollection<object> TopRow { get; } = [];

    /// <summary>Units in no node, or in one that is missing, dissolved or a Partner.</summary>
    public ObservableCollection<BoardUnitRow> UnplacedUnits { get; } = [];

    public ObservableCollection<FreeTextSectionCard> FreeTextSections { get; } = [];

    /// <summary>The Führungsspanne hints for the Einsatzleitung itself; a node's hint sits on its card.</summary>
    public ObservableCollection<string> Hints { get; } = [];

    [ObservableProperty]
    private bool _hasPartners;

    [ObservableProperty]
    private bool _hasFachberater;

    [ObservableProperty]
    private bool _hasUnplacedUnits;

    [ObservableProperty]
    private bool _hasFreeTextSections;

    private void Refresh()
    {
        var incident = _session.Incident;
        _incident = incident;
        var line = ReportingLine.Of(incident);
        var hints = FuehrungsspanneHints.For(incident);
        _funkCheck = FunkCheck.Of(incident).ToDictionary(r => r.NodeId);
        var unplaced = ReportingLine.UnplacedUnits(incident);
        var hasBereitstellung = line.Any(e => e.Kind == ReportingEntryKind.Bereitstellung);
        _waiting = hasBereitstellung ? unplaced.Where(u => IsBereitstellungStatus(u.Status)).ToList() : [];

        if (line.FirstOrDefault(e => e.Kind == ReportingEntryKind.Einsatzleitung) is { } el)
        {
            El.Update(el, incident.LeadershipPost);
        }

        var children = NodeEntries(line, e => e.Depth == 2);
        ReconcileCards(
            Sections,
            NodeEntries(line, e => e.Depth == 1 && e.Kind != ReportingEntryKind.Partner),
            incident,
            hints,
            node => children.Where(c => c.Node.ParentId == node.Id).ToList());
        ReconcileCards(Partners, NodeEntries(line, e => e.Kind == ReportingEntryKind.Partner), incident, hints, _ => []);

        BoardPersonRow.Reconcile(
            Fachberater,
            line.Where(e => e.Kind == ReportingEntryKind.Fachberater).SelectMany(e => e.Others).ToList());
        ReconcileUnits(UnplacedUnits, unplaced.Where(u => !IsIn(_waiting, u)).ToList(), incident);
        RowReconciler.Reconcile(
            FreeTextSections,
            ReportingLine.FreeTextSections(incident),
            c => FreeTextSectionCard.KeyOf(c.Section),
            s => FreeTextSectionCard.KeyOf(s.Section),
            s => new FreeTextSectionCard(
                s.Section, s.Assignments, IsReadOnly ? null : AdoptFreeTextSection, IsReadOnly ? null : OpenAssignment),
            (c, s) => c.Update(s.Section, s.Assignments));
        RowReconciler.Reconcile(
            Hints,
            hints.Where(h => h.NodeId is null).Select(h => h.Text).Distinct(StringComparer.Ordinal).ToList(),
            h => h,
            h => h,
            h => h,
            (_, _) => { });

        HasPartners = Partners.Count > 0;
        HasFachberater = Fachberater.Count > 0;

        // The tips change exactly when a preset's button turns on or off; a refresh that changed
        // neither raises nothing.
        var tips = (PartnerTip("Rettungsdienst"), PartnerTip("Polizei"));
        if (tips != (RettungsdienstTip, PolizeiTip))
        {
            (RettungsdienstTip, PolizeiTip) = tips;
            AddPartnerCommand.NotifyCanExecuteChanged();
        }

        List<object> top = [El, .. Partners];
        if (HasFachberater)
        {
            top.Add(_fachberaterGroup);
        }

        RowReconciler.Reconcile(TopRow, top, r => r, i => i, i => i, (_, _) => { });
        HasUnplacedUnits = UnplacedUnits.Count > 0;
        HasFreeTextSections = FreeTextSections.Count > 0;
    }

    // The reporting-line rows that stand for a command node, paired with it so no caller has to
    // re-check Node for null.
    private static List<(ReportingEntry Entry, CommandNode Node)> NodeEntries(
        IEnumerable<ReportingEntry> line, Func<ReportingEntry, bool> filter)
    {
        var entries = new List<(ReportingEntry Entry, CommandNode Node)>();
        foreach (var entry in line)
        {
            if (entry.Node is { } node && filter(entry))
            {
                entries.Add((entry, node));
            }
        }

        return entries;
    }

    private void ReconcileCards(
        ObservableCollection<BoardNodeCard> cards,
        IReadOnlyList<(ReportingEntry Entry, CommandNode Node)> entries,
        Incident incident,
        IReadOnlyList<SpanHint> hints,
        Func<CommandNode, IReadOnlyList<(ReportingEntry Entry, CommandNode Node)>> childrenOf) =>
        RowReconciler.Reconcile(
            cards,
            entries,
            c => c.Id,
            e => e.Node.Id,
            e =>
            {
                var card = new BoardNodeCard(
                    e.Node.Id, RequestDissolve, EditNode, AddUnterabschnitt, (c, slot) => OpenPerson(c.Id, slot));
                UpdateCard(card, e, incident, hints, childrenOf);
                return card;
            },
            (card, e) => UpdateCard(card, e, incident, hints, childrenOf));

    private void UpdateCard(
        BoardNodeCard card,
        (ReportingEntry Entry, CommandNode Node) e,
        Incident incident,
        IReadOnlyList<SpanHint> hints,
        Func<CommandNode, IReadOnlyList<(ReportingEntry Entry, CommandNode Node)>> childrenOf)
    {
        var hint = hints.FirstOrDefault(h => h.NodeId == e.Node.Id)?.Text;
        card.Update(
            e.Entry, e.Node, hint, BoardNodeCard.BlockedReason(incident, e.Node), IsReadOnly, _funkCheck.GetValueOrDefault(e.Node.Id));

        // The Stärke counts the placed units only: a waiting unit stands in every Bereitstellungsraum
        // and would be summed once per card.
        card.UpdateStrength(NodeStrength.Of(incident, e.Node.Id), childrenOf(e.Node).Count > 0);
        if (e.Node.Kind == CommandNodeKind.Bereitstellung)
        {
            ReconcileUnits(card.Units, [.. e.Entry.Units, .. _waiting], incident, _waiting);
        }
        else
        {
            ReconcileUnits(card.Units, e.Entry.Units, incident);
        }

        // Unterabschnitte go one level deep: their own children are never asked for.
        ReconcileCards(card.Children, childrenOf(e.Node), incident, hints, _ => []);
    }

    private void ReconcileUnits(
        ObservableCollection<BoardUnitRow> rows,
        IReadOnlyList<ForceUnit> units,
        Incident incident,
        IReadOnlyList<ForceUnit>? waiting = null) =>
        RowReconciler.Reconcile(
            rows,
            units,
            r => r.Id,
            u => u.Id,
            u => new BoardUnitRow(u, incident, IsReadOnly, nodeId => MoveUnit(u.Id, nodeId), IsIn(waiting, u)),
            (r, u) => r.Update(u, incident, IsIn(waiting, u)));

    private static bool IsIn(IReadOnlyList<ForceUnit>? units, ForceUnit unit) =>
        units is not null && units.Any(u => u.Id == unit.Id);

    // #825: the status is a free Stammdaten string, so the Bereitstellungsraum is recognised by its
    // name, the way the sample Stammdaten spell it.
    private static bool IsBereitstellungStatus(string? status) =>
        string.Equals(status?.Trim(), "Bereitstellungsraum", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="unitId"/> may go to <paramref name="nodeId"/> (null = unplace it): the
    /// Einsatz is open, the unit is known, the target is one of the nodes the picker offers and is
    /// not where the unit already sits.
    /// </summary>
    public bool CanMoveUnit(Guid unitId, Guid? nodeId)
    {
        if (IsReadOnly)
        {
            return false;
        }

        var unit = _incident.Forces.FirstOrDefault(u => u.Id == unitId);
        if (unit is null)
        {
            return false;
        }

        var choices = NodeChoices.Build(_incident, unit.NodeId, _ => { });
        return choices.Any(c => c.NodeId == nodeId) && NodeChoices.CurrentNodeId(choices) != nodeId;
    }

    /// <summary>
    /// Moves a unit and offers the move back, place and status together: one stray drop changes both.
    /// False when <see cref="CanMoveUnit"/> says no or the session refuses.
    /// </summary>
    public bool MoveUnit(Guid unitId, Guid? nodeId)
    {
        ActionError = null;
        if (!CanMoveUnit(unitId, nodeId))
        {
            return false;
        }

        // Read before the move: a joined client's Incident may not show it yet afterwards.
        // Stryker disable once Linq : CanMoveUnit already found the unit, so First and FirstOrDefault agree.
        var unit = _incident.Forces.First(u => u.Id == unitId);
        var (fromNodeId, fromStatus) = (unit.NodeId, unit.Status);
        var title = string.IsNullOrWhiteSpace(unit.CallSign) ? unit.Brigade : unit.CallSign;
        var target = nodeId is { } id ? _incident.FindCommandNode(id) : null;
        var from = fromNodeId is { } fid ? _incident.FindCommandNode(fid) : null;
        var status = target is null ? null : UnitPlacement.ImpliedStatus(target.Kind, _masterData.UnitStatus);
        if (!Run(() => UnitPlacement.Move(_session, _masterData.UnitStatus, unitId, nodeId)))
        {
            return false;
        }

        var statusChanged = status is not null && !string.Equals(status, fromStatus, StringComparison.Ordinal);
        var message = target is null
            ? $"„{title}“ aus „{from?.Name}“ genommen."
            : statusChanged
                ? $"„{title}“ in „{target.Name}“ eingeordnet, Status „{status}“."
                : $"„{title}“ in „{target.Name}“ eingeordnet.";
        _offerUndo(message, () => Run(() =>
        {
            // Straight back to the old place: UnitPlacement would give it that place's status, not the
            // one it had. Whether the status changed is known from the move itself, since a joined
            // client's Incident may not show the new status yet.
            _session.MoveForceUnit(unitId, fromNodeId);
            if (statusChanged)
            {
                // The notes as they stand now, so a peer's edit since the move stays. MoveForceUnit
                // has just found the unit, or thrown into Run; a gone unit throws here into Run too.
                // Stryker disable once Linq : the unit is there, so First and FirstOrDefault agree.
                var notes = _session.Incident.Forces.First(u => u.Id == unitId).Notes;
                _session.UpdateForceUnit(unitId, fromStatus, notes);
            }
        }));
        return true;
    }

    /// <summary>
    /// Whether <paramref name="unitId"/> may become the Einsatzleitung's Führungsfahrzeug by a drop
    /// on its card (#888): the Einsatz is open, the unit is known, has a Funkrufname, and is not the
    /// Führungsfahrzeug already.
    /// </summary>
    public bool CanAssignFuehrungsfahrzeug(Guid unitId) =>
        !IsReadOnly
        && _incident.Forces.FirstOrDefault(u => u.Id == unitId)?.CallSign is { } callSign
        && !string.Equals(callSign, _incident.LeadershipPost.Fuehrungsfahrzeug, StringComparison.Ordinal);

    /// <summary>
    /// Makes the unit's Funkrufname the Einsatzleitung's Führungsfahrzeug, as the post dialog (#829)
    /// does: the post's other fields are kept, and the unit keeps its place and status. False when
    /// <see cref="CanAssignFuehrungsfahrzeug"/> says no or the session refuses.
    /// </summary>
    public bool AssignFuehrungsfahrzeug(Guid unitId)
    {
        ActionError = null;
        if (!CanAssignFuehrungsfahrzeug(unitId))
        {
            return false;
        }

        var callSign = _incident.Forces.First(u => u.Id == unitId).CallSign;
        var post = _incident.LeadershipPost;
        var previous = post.Fuehrungsfahrzeug;
        if (!Run(() => _session.UpdateLeadershipPost(post.Standort, post.Rufname, post.Telefon, post.Funkgruppen, callSign)))
        {
            return false;
        }

        // The post as it stands by then: only the Fahrzeug goes back, an edit made meanwhile stays.
        _offerUndo($"„{callSign}“ ist Führungsfahrzeug der Einsatzleitung.", () => Run(() =>
        {
            var now = _session.Incident.LeadershipPost;
            _session.UpdateLeadershipPost(now.Standort, now.Rufname, now.Telefon, now.Funkgruppen, previous);
        }));
        return true;
    }

    /// <summary>A drag of <paramref name="unitId"/> began; ignored on a read-only Einsatz.</summary>
    public void BeginDrag(Guid unitId)
    {
        if (!IsReadOnly)
        {
            DraggingUnitId = unitId;
        }
    }

    public void EndDrag() => DraggingUnitId = null;

    /// <summary>
    /// Whether <paramref name="nodeId"/> may go under <paramref name="parentId"/> (null: the top level)
    /// before <paramref name="beforeNodeId"/> (null: at the end): the Einsatz is open, the domain's
    /// <see cref="CommandNodeMoves.Refusal"/> allows it, and it would change something.
    /// </summary>
    public bool CanMoveNode(Guid nodeId, Guid? parentId, Guid? beforeNodeId)
    {
        if (IsReadOnly || CommandNodeMoves.Refusal(_incident, nodeId, parentId, beforeNodeId) is not null)
        {
            return false;
        }

        var node = _incident.FindCommandNode(nodeId);
        if (node?.ParentId != parentId)
        {
            return true;
        }

        // Same Abschnitt: only a new place among the siblings is a move.
        return beforeNodeId != nodeId && beforeNodeId != NextSibling(nodeId, parentId);
    }

    // The live sibling right after nodeId under parentId; null when it is the last.
    private Guid? NextSibling(Guid nodeId, Guid? parentId)
    {
        var siblings = _incident.CommandNodes.Where(n => n.IsLive && CommandNodeMoves.IsSiblingAt(n, parentId)).Select(n => n.Id).ToList();
        var index = siblings.IndexOf(nodeId);
        return index + 1 < siblings.Count ? siblings[index + 1] : null;
    }

    /// <summary>
    /// Moves a node and offers the move back; false when <see cref="CanMoveNode"/> says no or the
    /// session refuses.
    /// </summary>
    public bool MoveNode(Guid nodeId, Guid? parentId, Guid? beforeNodeId)
    {
        ActionError = null;
        if (!CanMoveNode(nodeId, parentId, beforeNodeId) || _incident.FindCommandNode(nodeId) is not { } node)
        {
            return false;
        }

        // Where it stands now: under its parent, before the sibling that follows it (null: last).
        var fromParentId = node.ParentId;
        var fromBeforeId = NextSibling(nodeId, fromParentId);
        if (!Run(() => _session.MoveCommandNode(nodeId, parentId, beforeNodeId)))
        {
            return false;
        }

        _offerUndo($"„{node.Name}“ verschoben.", () => Run(() => _session.MoveCommandNode(nodeId, fromParentId, fromBeforeId)));
        return true;
    }

    /// <summary>A drag of the card of <paramref name="nodeId"/> began; ignored on a read-only Einsatz.</summary>
    public void BeginNodeDrag(Guid nodeId)
    {
        if (!IsReadOnly)
        {
            DraggingNodeId = nodeId;
        }
    }

    public void EndNodeDrag() => DraggingNodeId = null;

    private bool CanAdd => !IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddAbschnitt() =>
        OpenCreate(CommandNodeKind.Abschnitt, null, SuggestName("Abschnitt", alwaysNumbered: true));

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddBereitstellung() =>
        OpenCreate(CommandNodeKind.Bereitstellung, null, SuggestName("Bereitstellung", alwaysNumbered: false));

    /// <param name="preset">The organisation's name ("Rettungsdienst", "Polizei"), or blank for "Weitere".</param>
    [RelayCommand(CanExecute = nameof(CanAddPartner))]
    private void AddPartner(string? preset) =>
        OpenCreate(CommandNodeKind.Partner, null, preset ?? string.Empty);

    // A preset organisation is added once: a second could not be saved under its name anyway, so
    // its button switches off rather than open a dialog bound to fail. "Weitere" stays on.
    private bool CanAddPartner(string? preset) =>
        CanAdd && (string.IsNullOrWhiteSpace(preset) || !HasLivePartner(preset));

    private bool HasLivePartner(string name) =>
        _incident.CommandNodes.Any(n =>
            n.IsLive && n.Kind == CommandNodeKind.Partner && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    private string PartnerTip(string preset) =>
        HasLivePartner(preset) ? $"Einsatzleitung {preset} ist bereits eingetragen" : $"Einsatzleitung {preset} bilden";

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddFachberater() => OpenPerson(null, RoleSlot.Fachberater);

    private void OpenPerson(Guid? nodeId, RoleSlot slot) =>
        _openPersonDialog?.Invoke(new LeadershipPersonDialogViewModel(_session, _clock, _masterData, nodeId, slot, _onChanged));

    // #824: the card's people move into the Abschnitt of that name, formed when there is none.
    private void AdoptFreeTextSection(string section) => Run(() => _session.AdoptFreeTextSection(section));

    private void OpenAssignment(Guid assignmentId, RoleAssignmentDialogMode mode) =>
        _openAssignmentDialog?.Invoke(new RoleAssignmentDialogViewModel(_session, _masterData, assignmentId, mode, _onChanged));

    private void AddUnterabschnitt(BoardNodeCard parent) =>
        OpenCreate(CommandNodeKind.Unterabschnitt, parent.Id, SuggestName("Unterabschnitt", alwaysNumbered: true, parent.Id));

    private void EditNode(BoardNodeCard card) =>
        _openDialog?.Invoke(CommandNodeDialogViewModel.ForEdit(_session, _masterData, card.Id, _onChanged));

    private void EditPost() =>
        _openDialog?.Invoke(CommandNodeDialogViewModel.ForPost(_session, _masterData, _onChanged));

    private void OpenCreate(CommandNodeKind kind, Guid? parentId, string suggestedName) =>
        _openDialog?.Invoke(CommandNodeDialogViewModel.ForCreate(_session, _masterData, kind, parentId, suggestedName, _onChanged));

    // "Abschnitt 1", "Abschnitt 2", ...: the lowest number no live top-level node carries. A
    // dissolved node's name is free again. Without alwaysNumbered the bare prefix comes first.
    // The first free "{prefix} {n}" among the live nodes that share the name's namespace: the top
    // level, or one Abschnitt's Unterabschnitte (<paramref name="parentId"/>).
    private string SuggestName(string prefix, bool alwaysNumbered, Guid? parentId = null)
    {
        var taken = _session.Incident.CommandNodes
            .Where(n => n.IsLive && n.ParentId == parentId)
            .Select(n => n.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!alwaysNumbered && !taken.Contains(prefix))
        {
            return prefix;
        }

        for (var n = alwaysNumbered ? 1 : 2; ; n++)
        {
            var candidate = $"{prefix} {n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void RequestDissolve(BoardNodeCard card)
    {
        ActionError = null;
        var id = card.Id;
        void Dissolve() => Run(() => _session.DissolveCommandNode(id));

        if (_requestConfirm is null)
        {
            Dissolve();
            return;
        }

        _requestConfirm(
            $"{card.KindLabel} auflösen?",
            $"„{card.Name}“ wird aufgelöst. Das Einsatztagebuch behält den Eintrag.",
            Dissolve,
            "AUFLÖSEN");
    }

    // Runs a session command and tells the host. The Domain's refusals are German sentences about a
    // state a peer got to first (a node dissolved a moment ago), so they are shown as they are.
    private bool Run(Action command)
    {
        ActionError = null;
        try
        {
            command();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            ActionError = DomainRefusal.Message(ex);
            return false;
        }

        _onChanged();
        return true;
    }
}
