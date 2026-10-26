using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One live command node on the Führung board: a Bereitstellung, an Abschnitt (its Unterabschnitte
/// in <see cref="Children"/>), an Unterabschnitt, or a partner's Einsatzleitung. Kept for as long as
/// its node is shown and updated in place (#294); <see cref="LeadershipViewModel"/> reconciles
/// <see cref="Units"/> and <see cref="Children"/>, which need its callbacks.
/// </summary>
public sealed partial class BoardNodeCard : ObservableObject
{
    /// <param name="id">The node this card shows.</param>
    /// <param name="onDissolve">Asks the board to dissolve the node; the card only offers the command.</param>
    /// <param name="onEdit">Opens the edit dialog for the node.</param>
    /// <param name="onAddUnterabschnitt">Opens the dialog to form an Unterabschnitt in this Abschnitt.</param>
    /// <param name="onPerson">Opens the person dialog for this node and the given post.</param>
    public BoardNodeCard(
        Guid id,
        Action<BoardNodeCard>? onDissolve = null,
        Action<BoardNodeCard>? onEdit = null,
        Action<BoardNodeCard>? onAddUnterabschnitt = null,
        Action<BoardNodeCard, RoleSlot>? onPerson = null)
    {
        Id = id;
        DissolveCommand = new RelayCommand(() => onDissolve?.Invoke(this), () => CanDissolve);
        EditCommand = new RelayCommand(() => onEdit?.Invoke(this), () => !_isReadOnly);
        AddUnterabschnittCommand = new RelayCommand(
            () => onAddUnterabschnitt?.Invoke(this), () => !_isReadOnly && Kind == CommandNodeKind.Abschnitt);
        SetLeiterCommand = new RelayCommand(() => onPerson?.Invoke(this, RoleSlot.Leiter), () => !_isReadOnly);
        SetMelderCommand = new RelayCommand(() => onPerson?.Invoke(this, RoleSlot.Melder), () => !_isReadOnly);
        AddMemberCommand = new RelayCommand(() => onPerson?.Invoke(this, RoleSlot.None), () => !_isReadOnly);
    }

    private bool _isReadOnly = true;

    /// <summary>Opens the node's edit dialog; cannot execute on a read-only Einsatz.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Forms an Unterabschnitt in this node; only an Abschnitt can hold one.</summary>
    public IRelayCommand AddUnterabschnittCommand { get; }

    /// <summary>Sets the node's Leiter, handing over from a running one; off on a read-only Einsatz.</summary>
    public IRelayCommand SetLeiterCommand { get; }

    /// <summary>Sets the node's Melder, handing over from a running one; off on a read-only Einsatz.</summary>
    public IRelayCommand SetMelderCommand { get; }

    /// <summary>Adds another Funktion to the node (a GF, a Sicherheitsbeauftragter); off on a read-only Einsatz.</summary>
    public IRelayCommand AddMemberCommand { get; }

    /// <summary>Dissolves the node; can execute exactly when <see cref="CanDissolve"/> is true.</summary>
    public IRelayCommand DissolveCommand { get; }

    public Guid Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    [NotifyPropertyChangedFor(nameof(IsAbschnitt))]
    [NotifyPropertyChangedFor(nameof(LeiterLabel))]
    [NotifyPropertyChangedFor(nameof(SetLeiterTip))]
    [NotifyPropertyChangedFor(nameof(IsMovable))]
    [NotifyPropertyChangedFor(nameof(IsUnterabschnitt))]
    private CommandNodeKind _kind;

    /// <summary>Whether the card is an Unterabschnitt, drawn a shade lighter below its Abschnitt.</summary>
    public bool IsUnterabschnitt => Kind == CommandNodeKind.Unterabschnitt;

    /// <summary>The Abschnitt an Unterabschnitt lies in; null for every other card. A drop before this card lands here too.</summary>
    [ObservableProperty]
    private Guid? _parentId;

    /// <summary>Whether the card can be picked up and moved; a partner's Einsatzleitung has a row of its own.</summary>
    public bool IsMovable => Kind != CommandNodeKind.Partner;

    /// <summary>The SUMME row's Stärke, Zugführer / Gruppenführer / Mannschaft / Gesamt, an Abschnitt's Unterabschnitte included.</summary>
    [ObservableProperty]
    private string _strengthTotal = string.Empty;

    /// <summary>The SUMME row's AGT.</summary>
    [ObservableProperty]
    private int _scbaTotal;

    /// <summary>Whether any unit adds to the sum; a card without one shows no SUMME row.</summary>
    [ObservableProperty]
    private bool _hasStrengthTotal;

    /// <summary>
    /// "SUMME", or "SUMME INKL. UA" while the card has Unterabschnitte: they hang below it on the
    /// chart, so the row has to say it counts them.
    /// </summary>
    [ObservableProperty]
    private string _strengthLabel = "SUMME";

    /// <summary>Takes the node's summed Stärke; see <see cref="NodeStrength"/>.</summary>
    public void UpdateStrength(NodeStrength strength, bool hasUnterabschnitte)
    {
        ArgumentNullException.ThrowIfNull(strength);
        StrengthTotal = strength.Text;
        ScbaTotal = strength.Scba;
        HasStrengthTotal = strength.UnitCount > 0;
        StrengthLabel = hasUnterabschnitte ? "SUMME INKL. UA" : "SUMME";
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _auftrag;

    [ObservableProperty]
    private string? _funkgruppe;

    [ObservableProperty]
    private string? _standort;

    [ObservableProperty]
    private string? _rufname;

    [ObservableProperty]
    private string? _telefon;

    [ObservableProperty]
    private BoardPersonRow? _leiter;

    [ObservableProperty]
    private BoardPersonRow? _melder;

    /// <summary>The Führungsspanne hint for this node, if FwDV 100's limit is exceeded.</summary>
    [ObservableProperty]
    private string? _hint;

    [ObservableProperty]
    private bool _canDissolve;

    partial void OnCanDissolveChanged(bool value) => DissolveCommand.NotifyCanExecuteChanged();

    /// <summary>Why the node cannot be dissolved yet; null when it can, or on a read-only Einsatz.</summary>
    [ObservableProperty]
    private string? _dissolveBlockedReason;

    /// <summary>Whether the card is an Abschnitt, the one kind that offers to form an Unterabschnitt.</summary>
    public bool IsAbschnitt => Kind == CommandNodeKind.Abschnitt;

    /// <summary>What the card says the node is; a partner's node is an Einsatzleitung.</summary>
    public string KindLabel => CommandNodeKindLabels.Of(Kind);

    /// <summary>The Leiter row's label, as said for this kind of node: EAL, UAL, EL or LEITER.</summary>
    public string LeiterLabel => LeiterLabels.Of(Kind).ToUpperInvariant();

    /// <summary>The Leiter button's tooltip and automation name, e.g. "EAL festlegen".</summary>
    public string SetLeiterTip => $"{LeiterLabels.Of(Kind)} festlegen";

    /// <summary>Running Funktionen in the node beside its Leiter and Melder: no post, or one held twice.</summary>
    public ObservableCollection<BoardPersonRow> Members { get; } = [];

    public ObservableCollection<BoardUnitRow> Units { get; } = [];

    /// <summary>An Abschnitt's live Unterabschnitte; empty for every other card.</summary>
    public ObservableCollection<BoardNodeCard> Children { get; } = [];

    /// <summary>
    /// Why <paramref name="node"/> cannot be dissolved, mirroring the refusal in
    /// <see cref="Incident.DissolveCommandNode"/>, or null when it can.
    /// </summary>
    internal static string? BlockedReason(Incident incident, CommandNode node)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(node);

        if (incident.Forces.Any(u => u.NodeId == node.Id))
        {
            return "Erst die Einheiten verschieben.";
        }

        if (incident.Roles.Any(r => r.To is null && r.NodeId == node.Id))
        {
            return "Erst die Funktionen beenden.";
        }

        return incident.CommandNodes.Any(n => n.IsLive && n.ParentId == node.Id)
            ? "Erst die Unterabschnitte auflösen."
            : null;
    }

    /// <summary>Takes the node's details and people from its reporting-line row.</summary>
    public void Update(ReportingEntry entry, CommandNode node, string? hint, string? blockedReason, bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(node);

        _isReadOnly = isReadOnly;
        Kind = node.Kind;
        ParentId = node.ParentId;
        Name = node.Name;
        Auftrag = node.Auftrag;
        Funkgruppe = node.Funkgruppe;
        Standort = node.Standort;
        Rufname = node.Rufname;
        Telefon = node.Telefon;
        Leiter = BoardPersonRow.Follow(Leiter, entry.Leiter);
        Melder = BoardPersonRow.Follow(Melder, entry.Melder);
        BoardPersonRow.Reconcile(Members, entry.Others);
        Hint = hint;
        DissolveBlockedReason = isReadOnly ? null : blockedReason;
        CanDissolve = !isReadOnly && blockedReason is null;
        EditCommand.NotifyCanExecuteChanged();
        AddUnterabschnittCommand.NotifyCanExecuteChanged();
        SetLeiterCommand.NotifyCanExecuteChanged();
        SetMelderCommand.NotifyCanExecuteChanged();
        AddMemberCommand.NotifyCanExecuteChanged();
    }
}
