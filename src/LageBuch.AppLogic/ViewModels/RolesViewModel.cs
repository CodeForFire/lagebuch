using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
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

    public RoleAssignmentRow(
        Guid id,
        string role,
        string personName,
        string? section,
        string? callSign,
        string? phone,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool isReadOnly,
        Action<RoleAssignmentRow> onTransfer,
        Action<RoleAssignmentRow, string?> onPhoneEdited)
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

public sealed partial class RolesViewModel : ObservableObject, IDisposable
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

    public void Dispose() => _session.Changed -= RefreshRoles;

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
        {
            if (ShowAllRoles || row.IsRunning)
            {
                Roles.Add(row);
            }
        }
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

    // The dock and the handover panel below the grid are two separate forms, so each keeps its own
    // "the operator has asked" state: pressing one must not light up a field in the other (#412).
    private bool _addErrorsShown;

    private bool _transferErrorsShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewRoleError))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    [NotifyPropertyChangedFor(nameof(IsNewRoleUnknown))]
    private string _newRole = string.Empty;

    /// <summary>
    /// Whether the typed Funktion is absent from the Stammdaten — drives a hint, never a block.
    /// Funktion is deliberately free text (an ad-hoc or mutual-aid role must stay enterable), so
    /// this only points out that the entry will not match the rest; the assignment is made either
    /// way. False while no Funktionen are configured at all, where it would flag everything.
    /// </summary>
    public bool IsNewRoleUnknown => StammdatenCatalogue.IsUnknown(NewRole, RoleOptions);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewPersonNameError))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    private string _newPersonName = string.Empty;

    [ObservableProperty]
    private string? _newSection;

    [ObservableProperty]
    private string? _newCallSign;

    [ObservableProperty]
    private string? _newPhone;

    partial void OnNewPersonNameChanged(string value) =>
        PrefillFromRoster(value, () => NewPhone, v => NewPhone = v, () => NewCallSign, v => NewCallSign = v);

    /// <summary>Whether the Funktion is still missing, once the operator has asked (#412).</summary>
    /// <summary>Everything this form is still waiting on, on one line beneath its fields (#412).</summary>
    public string? ErrorSummary => ValidationMessages.Summarize(
        NewRoleError, NewPersonNameError);

    public string? NewRoleError =>
        _addErrorsShown && string.IsNullOrWhiteSpace(NewRole) ? ValidationMessages.Required : null;

    /// <summary>Whether the Name is still missing, once the operator has asked (#412).</summary>
    public string? NewPersonNameError =>
        _addErrorsShown && string.IsNullOrWhiteSpace(NewPersonName) ? ValidationMessages.Required : null;

    // Only the read-only rule gates the button; the empty fields answer on the press (#412). Note
    // this is separate from IsNewRoleUnknown, which never blocked anything and still does not.
    private bool CanAddRole => !IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanAddRole))]
    private void AddRole()
    {
        if (!ValidateAdd())
        {
            return;
        }

        // Von is stamped rather than typed: an assignment is recorded at the moment it happens,
        // and every other time in this application comes from the injected clock the same way.
        //
        // The Funktion adopts the Stammdaten spelling when it matches one apart from case or
        // spacing, so "el" and "EL " do not become two more Funktionen alongside the configured
        // "EL". An unrecognised one is assigned as typed -- see IsNewRoleUnknown.
        _session.AssignRole(
            StammdatenCatalogue.Normalize(NewRole, RoleOptions) ?? NewRole,
            NewPersonName,
            NewCallSign,
            from: _clock.Now,
            to: null,
            section: NewSection,
            phone: NewPhone); // Changed → RefreshRoles renders the row
        NewRole = string.Empty;
        NewPersonName = string.Empty;
        NewSection = null;
        NewCallSign = null;
        NewPhone = null;
        ShowAddErrors(false); // the cleared fields must not read as a fresh complaint
        _onChanged();
    }

    private bool ValidateAdd()
    {
        ShowAddErrors(true);
        return NewRoleError is null && NewPersonNameError is null;
    }

    private void ShowAddErrors(bool shown)
    {
        _addErrorsShown = shown;
        OnPropertyChanged(nameof(NewRoleError));
        OnPropertyChanged(nameof(ErrorSummary));
        OnPropertyChanged(nameof(NewPersonNameError));
        OnPropertyChanged(nameof(ErrorSummary));
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
    [NotifyPropertyChangedFor(nameof(TransferPersonNameError))]
    [NotifyPropertyChangedFor(nameof(TransferErrorSummary))]
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
        ShowTransferErrors(false); // a freshly opened panel starts quiet
    }

    /// <summary>Whether the successor's name is still missing, once asked (#412).</summary>
    /// <summary>Everything this form is still waiting on, on one line beneath its fields (#412).</summary>
    public string? TransferErrorSummary => ValidationMessages.Summarize(
        TransferPersonNameError);

    public string? TransferPersonNameError =>
        _transferErrorsShown && string.IsNullOrWhiteSpace(TransferPersonName)
            ? ValidationMessages.Required
            : null;

    // IsTransferring stays: with no panel open there is no field to name.
    private bool CanConfirmTransfer => IsTransferring;

    [RelayCommand(CanExecute = nameof(CanConfirmTransfer))]
    private void ConfirmTransfer()
    {
        ShowTransferErrors(true);
        if (TransferPersonNameError is not null)
        {
            return;
        }

        _session.TransferRole(TransferringRow!.Id, TransferPersonName, TransferCallSign, TransferPhone); // Changed → RefreshRoles
        TransferringRow = null;
        ShowTransferErrors(false);
        _onChanged();
    }

    [RelayCommand]
    private void CancelTransfer()
    {
        TransferringRow = null;
        ShowTransferErrors(false);
    }

    private void ShowTransferErrors(bool shown)
    {
        _transferErrorsShown = shown;
        OnPropertyChanged(nameof(TransferPersonNameError));
        OnPropertyChanged(nameof(TransferErrorSummary));
    }

    /// <summary>
    /// Fills in what the roster knows about the person just picked, shared by the new-assignment
    /// name box and the transfer panel's. Only ever fills a blank field: a number typed by hand
    /// outranks the roster, which may be out of date.
    /// </summary>
    private void PrefillFromRoster(
        string personName,
        Func<string?> getPhone,
        Action<string?> setPhone,
        Func<string?> getCallSign,
        Action<string?> setCallSign)
    {
        var person = _personnel.FirstOrDefault(
            p => string.Equals(p.DisplayName, personName, StringComparison.OrdinalIgnoreCase));
        if (person is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(getPhone()))
        {
            setPhone(person.Phone);
        }

        if (string.IsNullOrWhiteSpace(getCallSign()))
        {
            setCallSign(person.CallSign);
        }
    }

    private RoleAssignmentRow CreateRow(Domain.RoleAssignment r) =>
        new(r.Id, r.Role, r.PersonName, r.Section, r.CallSign, r.Phone, r.From, r.To, IsReadOnly, BeginTransfer, EditPhone);

    private void EditPhone(RoleAssignmentRow row, string? phone)
    {
        _session.EditRolePhone(row.Id, phone); // Changed → RefreshRoles rebuilds the row
        _onChanged();
    }
}
