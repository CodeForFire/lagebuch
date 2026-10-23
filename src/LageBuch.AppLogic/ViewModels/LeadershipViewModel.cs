using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public LeadershipViewModel(IIncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        DispatchCentreName = masterData.Settings.DispatchCentreName;
        Refresh();
        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    [ObservableProperty]
    private bool _isNarrow;

    /// <summary>The Leitstelle the Einsatzleitung reports to, from the Stammdaten (default "ILS").</summary>
    public string DispatchCentreName { get; }

    public ElCard El { get; } = new();

    /// <summary>Bereitstellungen, Abschnitte and orphaned Unterabschnitte, in reporting-line order.</summary>
    public ObservableCollection<BoardNodeCard> Sections { get; } = [];

    /// <summary>The Einsatzleitungen of partner organisations (Rettungsdienst, Polizei, ...).</summary>
    public ObservableCollection<BoardNodeCard> Partners { get; } = [];

    public ObservableCollection<BoardPersonRow> Fachberater { get; } = [];

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
                var card = new BoardNodeCard(e.Node.Id);
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

    private void MoveUnit(Guid unitId, Guid? nodeId)
    {
        _session.MoveForceUnit(unitId, nodeId);
        _onChanged();
    }
}
