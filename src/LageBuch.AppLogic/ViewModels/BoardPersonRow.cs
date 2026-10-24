using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A person on the Führung board: a running Funktion shown as Leiter, Melder, member,
/// Führungsassistent or Fachberater. Observable so a corrected phone number updates the row in
/// place instead of replacing it under the view (#294).
/// </summary>
public sealed partial class BoardPersonRow : ObservableObject
{
    public BoardPersonRow(RoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        AssignmentId = assignment.Id;
        _role = assignment.Role;
        _personName = assignment.PersonName;
        _callSign = assignment.CallSign;
        _phone = assignment.Phone;
    }

    public Guid AssignmentId { get; }

    [ObservableProperty]
    private string _role;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private string _personName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private string? _callSign;

    [ObservableProperty]
    private string? _phone;

    /// <summary>"Name · Funkrufname", or the name alone when no Funkrufname is recorded.</summary>
    public string Text => string.IsNullOrWhiteSpace(CallSign) ? PersonName : $"{PersonName} · {CallSign}";

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
        System.Collections.ObjectModel.ObservableCollection<BoardPersonRow> rows, IReadOnlyList<RoleAssignment> assignments) =>
        RowReconciler.Reconcile(rows, assignments, r => r.AssignmentId, a => a.Id, a => new BoardPersonRow(a), (r, a) => r.Update(a));
}
