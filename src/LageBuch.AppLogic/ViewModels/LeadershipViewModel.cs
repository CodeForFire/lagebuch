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
/// Everything shown is derived by <see cref="ReportingLine"/>, <see cref="FuehrungsstufeAdvisor"/> and
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
    private readonly IClock _clock;
    private readonly FachberaterGroup _fachberaterGroup;

    // The incident as the last refresh showed it: the move guard judges against what the board offers.
    private Incident _incident;

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
        Action<RoleAssignmentDialogViewModel>? openAssignmentDialog = null)
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
        _clock = clock;
        _incident = session.Incident;
        IsReadOnly = session.IsReadOnly;
        El = new ElCard(SelectStufe, EditPost, !IsReadOnly, slot => OpenPerson(null, slot), OpenAssignment);
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

        if (line.FirstOrDefault(e => e.Kind == ReportingEntryKind.Einsatzleitung) is { } el)
        {
            El.Update(el, incident.LeadershipPost, incident.Fuehrungsstufe, FuehrungsstufeAdvisor.Suggest(incident));
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
        ReconcileUnits(UnplacedUnits, ReportingLine.UnplacedUnits(incident), incident);
        RowReconciler.Reconcile(
            FreeTextSections,
            ReportingLine.FreeTextSections(incident),
            c => FreeTextSectionCard.KeyOf(c.Section),
            s => FreeTextSectionCard.KeyOf(s.Section),
            s => new FreeTextSectionCard(s.Section, s.Assignments),
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
        card.Update(e.Entry, e.Node, hint, BoardNodeCard.BlockedReason(incident, e.Node), IsReadOnly);
        card.UpdateStrength(NodeStrength.Of(incident, e.Node.Id), childrenOf(e.Node).Count > 0);
        ReconcileUnits(card.Units, e.Entry.Units, incident);

        // Unterabschnitte go one level deep: their own children are never asked for.
        ReconcileCards(card.Children, childrenOf(e.Node), incident, hints, _ => []);
    }

    private void ReconcileUnits(ObservableCollection<BoardUnitRow> rows, IReadOnlyList<ForceUnit> units, Incident incident) =>
        RowReconciler.Reconcile(
            rows,
            units,
            r => r.Id,
            u => u.Id,
            u => new BoardUnitRow(u, incident, IsReadOnly, nodeId => MoveUnit(u.Id, nodeId)),
            (r, u) => r.Update(u, incident));

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

    /// <summary>Moves a unit; false when <see cref="CanMoveUnit"/> says no or the session refuses.</summary>
    public bool MoveUnit(Guid unitId, Guid? nodeId)
    {
        ActionError = null;
        if (!CanMoveUnit(unitId, nodeId))
        {
            return false;
        }

        return Run(() => _session.MoveForceUnit(unitId, nodeId));
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
        var siblings = _incident.CommandNodes.Where(n => n.IsLive && CommandNodeMoves.IsSiblingAt(n, parentId)).Select(n => n.Id).ToList();
        var index = siblings.IndexOf(nodeId);
        var next = index + 1 < siblings.Count ? siblings[index + 1] : (Guid?)null;
        return beforeNodeId != nodeId && beforeNodeId != next;
    }

    /// <summary>Moves a node; false when <see cref="CanMoveNode"/> says no or the session refuses.</summary>
    public bool MoveNode(Guid nodeId, Guid? parentId, Guid? beforeNodeId)
    {
        ActionError = null;
        return CanMoveNode(nodeId, parentId, beforeNodeId)
            && Run(() => _session.MoveCommandNode(nodeId, parentId, beforeNodeId));
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

    private void SelectStufe(Fuehrungsstufe? stufe)
    {
        ActionError = null;
        if (IsReadOnly || stufe == _incident.Fuehrungsstufe)
        {
            return;
        }

        Run(() => _session.SetFuehrungsstufe(stufe));
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
