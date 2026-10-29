using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A person on the Führung board: a running Funktion shown as Leiter, Melder, member,
/// Führungsassistent or Fachberater. Observable so a corrected phone number updates the row in
/// place instead of replacing it under the view (#294).
/// </summary>
public sealed partial class BoardPersonRow : ObservableObject
{
    /// <param name="assignment">The running Funktion the row shows.</param>
    /// <param name="onOpen">
    /// Opens the dialog that corrects or hands over this Funktion; null where the board offers
    /// neither (a Leiter or Melder post has its own FESTLEGEN, and a read-only Einsatz has none).
    /// </param>
    public BoardPersonRow(RoleAssignment assignment, Action<Guid, RoleAssignmentDialogMode>? onOpen = null)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        AssignmentId = assignment.Id;
        _role = assignment.Role;
        _personName = assignment.PersonName;
        _callSign = assignment.CallSign;
        _phone = assignment.Phone;
        EditCommand = new RelayCommand(() => onOpen?.Invoke(AssignmentId, RoleAssignmentDialogMode.Edit), () => onOpen is not null);
        TransferCommand = new RelayCommand(() => onOpen?.Invoke(AssignmentId, RoleAssignmentDialogMode.Transfer), () => onOpen is not null);
    }

    public Guid AssignmentId { get; }

    /// <summary>ÄNDERN: corrects the holder's name, Funkrufname or Handynummer.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>ÜBERTRAGEN: hands the Funktion over to someone else.</summary>
    public IRelayCommand TransferCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditAutomationName))]
    [NotifyPropertyChangedFor(nameof(TransferAutomationName))]
    private string _role;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    [NotifyPropertyChangedFor(nameof(EditAutomationName))]
    [NotifyPropertyChangedFor(nameof(TransferAutomationName))]
    private string _personName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private string? _callSign;

    [ObservableProperty]
    private string? _phone;

    /// <summary>"Name · Funkrufname", or the name alone when no Funkrufname is recorded.</summary>
    public string Text => string.IsNullOrWhiteSpace(CallSign) ? PersonName : $"{PersonName} · {CallSign}";

    /// <summary>What a screen reader calls ÄNDERN, so the buttons of several rows are told apart.</summary>
    public string EditAutomationName => $"{Role} ({PersonName}) ändern";

    /// <summary>What a screen reader calls ÜBERTRAGEN.</summary>
    public string TransferAutomationName => $"{Role} ({PersonName}) übertragen";

    /// <summary>
    /// The row for a single post (Leiter, Melder): <paramref name="current"/> brought up to date when it
    /// shows the same assignment, a new row when the post changed hands, null when it is vacant.
    /// </summary>
    public static BoardPersonRow? Follow(BoardPersonRow? current, RoleAssignment? assignment)
    {
        if (assignment is null)
        {
            return null;
        }

        if (current is not null && current.AssignmentId == assignment.Id)
        {
            current.Update(assignment);
            return current;
        }

        return new BoardPersonRow(assignment);
    }

    /// <summary>Takes the assignment's current values; <paramref name="assignment"/> may be a new instance.</summary>
    public void Update(RoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        Role = assignment.Role;
        PersonName = assignment.PersonName;
        CallSign = assignment.CallSign;
        Phone = assignment.Phone;
    }

    /// <summary>Brings <paramref name="rows"/> in line with <paramref name="assignments"/>, by id and in place.</summary>
    internal static void Reconcile(
        System.Collections.ObjectModel.ObservableCollection<BoardPersonRow> rows,
        IReadOnlyList<RoleAssignment> assignments,
        Action<Guid, RoleAssignmentDialogMode>? onOpen = null) =>
        RowReconciler.Reconcile(rows, assignments, r => r.AssignmentId, a => a.Id, a => new BoardPersonRow(a, onOpen), (r, a) => r.Update(a));
}
