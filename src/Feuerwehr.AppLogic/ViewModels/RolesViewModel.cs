using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Documents;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// One row of the Funktionszuweisung grid. Unlike the other read-only row records this one is
/// observable and carries a command, because an assignment can still be ended after it was
/// created — the same shape <see cref="ScbaTruppRow"/> uses, with the action supplied as a
/// callback so XAML binds a parameterless command.
/// </summary>
public sealed partial class RoleAssignmentRow : ObservableObject
{
    private readonly Action<RoleAssignmentRow> _onEnd;

    public RoleAssignmentRow(Guid id, string role, string personName, string? section,
        string? callSign, string? phone, DateTimeOffset? from, DateTimeOffset? to,
        bool isReadOnly, Action<RoleAssignmentRow> onEnd)
    {
        Id = id;
        Role = role;
        PersonName = personName;
        Section = section;
        CallSign = callSign;
        Phone = phone;
        From = from;
        IsReadOnly = isReadOnly;
        _onEnd = onEnd;
        To = to;
    }

    public Guid Id { get; }
    public string Role { get; }
    public string PersonName { get; }
    public string? Section { get; }
    public string? CallSign { get; }
    public string? Phone { get; }
    public DateTimeOffset? From { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToDisplay))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyCanExecuteChangedFor(nameof(EndCommand))]
    private DateTimeOffset? _to;

    public string FromDisplay => From is { } f ? Formatting.Timestamp(f) : "—";
    public string ToDisplay => To is { } t ? Formatting.Timestamp(t) : "—";

    /// <summary>True while the assignment is still active, i.e. has no Bis stamp yet.</summary>
    public bool IsRunning => To is null;

    private bool CanEnd => !IsReadOnly && IsRunning;

    [RelayCommand(CanExecute = nameof(CanEnd))]
    private void End() => _onEnd(this);
}

public sealed partial class RolesViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<Person> _personnel;

    public RolesViewModel(IncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _personnel = masterData.Personnel;
        IsReadOnly = session.IsReadOnly;
        RoleOptions = masterData.Roles;
        CallSignOptions = masterData.RadioCallSigns;
        PersonOptions = masterData.Personnel.Select(p => p.DisplayName).ToArray();
        Roles = new ObservableCollection<RoleAssignmentRow>(
            session.Incident.Roles.Select(CreateRow));
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<string> RoleOptions { get; }
    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>
    /// Suggestions for the name box. Empty when no personnel roster is installed, which is the
    /// normal state on a fresh clone — the box stays free text either way.
    /// </summary>
    public IReadOnlyList<string> PersonOptions { get; }

    public ObservableCollection<RoleAssignmentRow> Roles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRoleCommand))]
    private string _newRole = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRoleCommand))]
    private string _newPersonName = string.Empty;

    [ObservableProperty]
    private string? _newSection;

    [ObservableProperty]
    private string? _newCallSign;

    [ObservableProperty]
    private string? _newPhone;

    /// <summary>
    /// Fills in what the roster knows about the person just picked. Only ever fills a blank field:
    /// a number typed by hand outranks the roster, which may be out of date.
    /// </summary>
    partial void OnNewPersonNameChanged(string value)
    {
        var person = _personnel.FirstOrDefault(
            p => string.Equals(p.DisplayName, value, StringComparison.OrdinalIgnoreCase));
        if (person is null)
            return;
        if (string.IsNullOrWhiteSpace(NewPhone))
            NewPhone = person.Phone;
        if (string.IsNullOrWhiteSpace(NewCallSign))
            NewCallSign = person.CallSign;
    }

    private bool CanAddRole =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewRole) && !string.IsNullOrWhiteSpace(NewPersonName);

    [RelayCommand(CanExecute = nameof(CanAddRole))]
    private void AddRole()
    {
        // Von is stamped rather than typed: an assignment is recorded at the moment it happens,
        // and every other time in this application comes from the injected clock the same way.
        var role = _session.Incident.AssignRole(
            NewRole, NewPersonName, NewCallSign, from: _clock.Now, to: null,
            section: NewSection, phone: NewPhone);
        Roles.Add(CreateRow(role));
        NewRole = string.Empty;
        NewPersonName = string.Empty;
        NewSection = null;
        NewCallSign = null;
        NewPhone = null;
        _onChanged();
    }

    private RoleAssignmentRow CreateRow(Domain.RoleAssignment r) =>
        new(r.Id, r.Role, r.PersonName, r.Section, r.CallSign, r.Phone, r.From, r.To, IsReadOnly, EndAssignment);

    private void EndAssignment(RoleAssignmentRow row)
    {
        var ended = _session.Incident.EndRoleAssignment(row.Id, _clock.Now);
        row.To = ended.To;
        _onChanged();
    }
}
