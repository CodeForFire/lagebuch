using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Documents;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One row of the Funktionszuweisung grid. Unlike the other read-only row records this one is
/// observable: the phone number is a live cell (mirroring <see cref="ForceRow"/>'s Status/Notes),
/// and a running assignment carries a "Rolle übertragen" command supplied as a callback so XAML
/// binds a parameterless command, the same shape <see cref="ScbaTruppRow"/> uses.
/// </summary>
public sealed partial class RoleAssignmentRow : ObservableObject
{
    private readonly Action<RoleAssignmentRow> _onTransfer;
    private readonly Action<RoleAssignmentRow, string?> _onPhoneEdited;

    public RoleAssignmentRow(Guid id, string role, string personName, string? section,
        string? callSign, string? phone, DateTimeOffset? from, DateTimeOffset? to,
        bool isReadOnly, Action<RoleAssignmentRow> onTransfer, Action<RoleAssignmentRow, string?> onPhoneEdited)
    {
        Id = id;
        Role = role;
        PersonName = personName;
        Section = section;
        CallSign = callSign;
        From = from;
        IsReadOnly = isReadOnly;
        _onTransfer = onTransfer;
        _onPhoneEdited = onPhoneEdited;
        _phone = phone; // bypasses the setter below, so building the row doesn't push an edit
        To = to;
    }

    public Guid Id { get; }
    public string Role { get; }
    public string PersonName { get; }
    public string? Section { get; }
    public string? CallSign { get; }
    public DateTimeOffset? From { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    private string? _phone;

    /// <summary>Writes the correction straight through. A closed or remotely read-only incident is
    /// a historical record, so the push is skipped rather than throwing — mirrors ForceRow.Push().</summary>
    partial void OnPhoneChanged(string? value)
    {
        if (IsReadOnly)
            return;
        _onPhoneEdited(this, value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToDisplay))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyCanExecuteChangedFor(nameof(BeginTransferCommand))]
    private DateTimeOffset? _to;

    public string FromDisplay => From is { } f ? Formatting.Timestamp(f) : "—";
    public string ToDisplay => To is { } t ? Formatting.Timestamp(t) : "—";

    /// <summary>True while the assignment is still active, i.e. has no Bis stamp yet.</summary>
    public bool IsRunning => To is null;

    private bool CanBeginTransfer => !IsReadOnly && IsRunning;

    [RelayCommand(CanExecute = nameof(CanBeginTransfer))]
    private void BeginTransfer() => _onTransfer(this);
}

public sealed partial class RolesViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<Person> _personnel;

    // Every rendered row, regardless of the filter; Roles is the visible subset — mirrors
    // EtbViewModel's _all/Entries split, so ShowAllRoles can rebuild Roles without re-reading the
    // session.
    private readonly List<RoleAssignmentRow> _all = new();

    public RolesViewModel(IIncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _personnel = masterData.Personnel;
        IsReadOnly = session.IsReadOnly;
        RoleOptions = masterData.Roles;
        CallSignOptions = masterData.RadioCallSigns;
        PersonOptions = masterData.Personnel.Select(p => p.DisplayName).ToArray();
        Roles = new ObservableCollection<RoleAssignmentRow>();
        RefreshRoles();
        _session.Changed += RefreshRoles;
    }

    // Rebuild from the incident on any change — this device's edit, or (when joined) another's.
    private void RefreshRoles()
    {
        _all.Clear();
        _all.AddRange(_session.Incident.Roles.Select(CreateRow));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Roles.Clear();
        foreach (var row in _all)
            if (ShowAllRoles || row.IsRunning)
                Roles.Add(row);
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

    // Ended assignments are usually clutter once a handover happened, so they're hidden by
    // default — "nur aktuell" — and can be revealed on demand.
    [ObservableProperty]
    private bool _showAllRoles;

    partial void OnShowAllRolesChanged(bool value) => ApplyFilter();

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

    partial void OnNewPersonNameChanged(string value) =>
        PrefillFromRoster(value, () => NewPhone, v => NewPhone = v, () => NewCallSign, v => NewCallSign = v);

    private bool CanAddRole =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewRole) && !string.IsNullOrWhiteSpace(NewPersonName);

    [RelayCommand(CanExecute = nameof(CanAddRole))]
    private void AddRole()
    {
        // Von is stamped rather than typed: an assignment is recorded at the moment it happens,
        // and every other time in this application comes from the injected clock the same way.
        _session.AssignRole(
            NewRole, NewPersonName, NewCallSign, from: _clock.Now, to: null,
            section: NewSection, phone: NewPhone); // Changed → RefreshRoles renders the row
        NewRole = string.Empty;
        NewPersonName = string.Empty;
        NewSection = null;
        NewCallSign = null;
        NewPhone = null;
        _onChanged();
    }

    // --- Rolle übertragen: a small panel below the grid, mirroring EtbViewModel's edit panel
    //     rather than inline DataGrid cell editing — a handover needs its own person/call
    //     sign/phone, not a single cell. Replaces the old standalone "beenden" action; an
    //     assignment now only ends as part of a handover, or automatically when the incident closes. ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransferring))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmTransferCommand))]
    private RoleAssignmentRow? _transferringRow;

    public bool IsTransferring => TransferringRow is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmTransferCommand))]
    private string _transferPersonName = string.Empty;

    [ObservableProperty]
    private string? _transferCallSign;

    [ObservableProperty]
    private string? _transferPhone;

    partial void OnTransferPersonNameChanged(string value) =>
        PrefillFromRoster(value, () => TransferPhone, v => TransferPhone = v, () => TransferCallSign, v => TransferCallSign = v);

    private void BeginTransfer(RoleAssignmentRow row)
    {
        TransferringRow = row;
        TransferPersonName = string.Empty;
        TransferCallSign = null;
        TransferPhone = null;
    }

    private bool CanConfirmTransfer => IsTransferring && !string.IsNullOrWhiteSpace(TransferPersonName);

    [RelayCommand(CanExecute = nameof(CanConfirmTransfer))]
    private void ConfirmTransfer()
    {
        _session.TransferRole(TransferringRow!.Id, TransferPersonName, TransferCallSign, TransferPhone); // Changed → RefreshRoles
        TransferringRow = null;
        _onChanged();
    }

    [RelayCommand]
    private void CancelTransfer() => TransferringRow = null;

    /// <summary>
    /// Fills in what the roster knows about the person just picked, shared by the new-assignment
    /// name box and the transfer panel's. Only ever fills a blank field: a number typed by hand
    /// outranks the roster, which may be out of date.
    /// </summary>
    private void PrefillFromRoster(
        string personName, Func<string?> getPhone, Action<string?> setPhone,
        Func<string?> getCallSign, Action<string?> setCallSign)
    {
        var person = _personnel.FirstOrDefault(
            p => string.Equals(p.DisplayName, personName, StringComparison.OrdinalIgnoreCase));
        if (person is null)
            return;
        if (string.IsNullOrWhiteSpace(getPhone()))
            setPhone(person.Phone);
        if (string.IsNullOrWhiteSpace(getCallSign()))
            setCallSign(person.CallSign);
    }

    private RoleAssignmentRow CreateRow(Domain.RoleAssignment r) =>
        new(r.Id, r.Role, r.PersonName, r.Section, r.CallSign, r.Phone, r.From, r.To, IsReadOnly, BeginTransfer, EditPhone);

    private void EditPhone(RoleAssignmentRow row, string? phone)
    {
        _session.EditRolePhone(row.Id, phone); // Changed → RefreshRoles rebuilds the row
        _onChanged();
    }
}
