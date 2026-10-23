using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A unit on the Führung board, on its node's card or in the "Nicht zugeordnet" strip. It carries the
/// same Abschnitt picker as the Kräfte list's <see cref="ForceRow"/>, built by <see cref="ViewModels.NodeChoices"/>.
/// </summary>
public sealed partial class BoardUnitRow : ObservableObject
{
    private readonly bool _isReadOnly;
    private readonly Action<Guid?> _onMoved;

    // The node the unit counts as sitting in: a hostile NodeId (missing, dissolved, Partner) reads as
    // none, the same as ReportingLine.UnplacedUnits.
    private Guid? _placedNodeId;

    /// <param name="unit">The unit shown.</param>
    /// <param name="incident">The incident its picker offers the nodes of.</param>
    /// <param name="isReadOnly">A closed Einsatz is a record: a stray pick is inert instead of throwing.</param>
    /// <param name="onMoved">Moves the unit; called only for a node other than the current one.</param>
    public BoardUnitRow(ForceUnit unit, Incident incident, bool isReadOnly, Action<Guid?> onMoved)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(onMoved);
        Id = unit.Id;
        Brigade = unit.Brigade;
        CallSign = unit.CallSign;
        _isReadOnly = isReadOnly;
        _onMoved = onMoved;
        _strengthText = unit.StrengthText;
        _scbaCount = unit.ScbaCount;
        (NodeChoices, NodeLabel) = BuildNode(unit, incident);
        _placedNodeId = ViewModels.NodeChoices.CurrentNodeId(NodeChoices);
    }

    public Guid Id { get; }

    /// <summary>Not updated: no session operation changes a unit's Wache or Funkrufname.</summary>
    public string Brigade { get; }

    public string? CallSign { get; }

    /// <summary>The Funkrufname when there is one, the Wache otherwise.</summary>
    public string Title => string.IsNullOrWhiteSpace(CallSign) ? Brigade : CallSign;

    /// <summary>Zugführer/Führungskräfte/Mannschaft/Gesamt, as on the Kräfte list (#216).</summary>
    [ObservableProperty]
    private string _strengthText;

    [ObservableProperty]
    private int _scbaCount;

    /// <summary>The Abschnitt picker's entries; replaced only on a real change (#408).</summary>
    public IReadOnlyList<NodeChoice> NodeChoices { get; private set; }

    /// <summary>Where the unit sits; <see cref="ViewModels.NodeChoices.UnplacedLabel"/> when it is in no Abschnitt, so the picker reads as one.</summary>
    public string NodeLabel { get; private set; }

    /// <summary>Brings the row in line with its unit; <paramref name="unit"/> may be a new instance.</summary>
    public void Update(ForceUnit unit, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(incident);
        StrengthText = unit.StrengthText;
        ScbaCount = unit.ScbaCount;

        var (choices, label) = BuildNode(unit, incident);
        if (!ViewModels.NodeChoices.SameEntries(choices, NodeChoices))
        {
            NodeChoices = choices;
            _placedNodeId = ViewModels.NodeChoices.CurrentNodeId(choices);
            OnPropertyChanged(nameof(NodeChoices));
        }

        if (!string.Equals(label, NodeLabel, StringComparison.Ordinal))
        {
            NodeLabel = label;
            OnPropertyChanged(nameof(NodeLabel));
        }
    }

    private (IReadOnlyList<NodeChoice> Choices, string Label) BuildNode(ForceUnit unit, Incident incident)
    {
        var choices = ViewModels.NodeChoices.Build(incident, unit.NodeId, MoveTo);
        return (choices, ViewModels.NodeChoices.LabelOf(incident, choices));
    }

    private void MoveTo(Guid? nodeId)
    {
        if (_isReadOnly || nodeId == _placedNodeId)
        {
            return;
        }

        _onMoved(nodeId);
    }
}
