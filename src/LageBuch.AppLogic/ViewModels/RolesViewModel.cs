using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
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

    // Set while Update() writes the incident's values into the row, so the phone setter does not
    // push what was only just pulled back to the session as a new edit (#294).
    private bool _pulling;

    /// <summary>Writes the correction straight through. A closed or remotely read-only incident is
    /// a historical record, so the push is skipped rather than throwing — mirrors ForceRow.Push().</summary>
    partial void OnPhoneChanged(string? value)
    {
        if (IsReadOnly || _pulling)
            return;
        _onPhoneEdited(this, value);
    }

    /// <summary>
    /// Brings a kept row in line with its assignment after a change anywhere in the incident (#294),
    /// instead of the row being thrown away and rebuilt. Only Bis and the Handynummer can move on an
    /// existing assignment -- a handover ends it and starts a new one -- so the rest stays as built.
    /// </summary>
    public void Update(Domain.RoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        To = assignment.To;
        _pulling = true;
        try
        {
            Phone = assignment.Phone;
        }
        finally
        {
            _pulling = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToDisplay))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyCanExecuteChangedFor(nameof(BeginTransferCommand))]
    private DateTimeOffset? _to;

    /// <summary>
    /// Another running row holds the same Funktion — in the same Abschnitt too, when the Stammdaten
    /// mark that one unique once per Abschnitt (#470). Set by <c>RolesViewModel.MarkDuplicates</c>,
    /// never here: the grid reads this row, and while the rows are being built this one does not yet
    /// have its partner in the list, so a single pass would mark the second of a pair and not the
    /// first. A duplicate that arrived from an older Einsatzdatei, an import or a joined device is
    /// neither refused nor hidden — it is marked, and both rows of it are.
    /// </summary>
    [ObservableProperty]
    private bool _isDuplicate;

    public string FromDisplay => From is { } f ? Formatting.Timestamp(f) : "—";

    public string ToDisplay => To is { } t ? Formatting.Timestamp(t) : "—";

    /// <summary>True while the assignment is still active, i.e. has no Bis stamp yet.</summary>
    public bool IsRunning => To is null;

    private bool CanBeginTransfer => !IsReadOnly && IsRunning;

    [RelayCommand(CanExecute = nameof(CanBeginTransfer))]
    private void BeginTransfer() => _onTransfer(this);
}

public sealed partial class RolesViewModel : ObservableObject, INarrowAware, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<Person> _personnel;
    private readonly IReadOnlyList<Role> _roles;

    // Every rendered row, regardless of the filter; Roles is the visible subset — mirrors
    // EtbViewModel's _all/Entries split, so ShowAllRoles can reconcile Roles without re-reading the
    // session.
    private readonly List<RoleAssignmentRow> _all = new();

    // The two conflict messages, parsed once each: CompositeFormat is the cached form of a composite
    // template, and this hint is rebuilt on every keystroke in the add form. De is the fixed culture
    // Formatting applies to everything a person reads, repeated here because that class formats
    // timestamps and labels rather than a message template. Every value substituted is a string, so
    // the provider decides nothing either way today; it is there so the culture is never the
    // device's, whatever a future argument turns out to be.
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly CompositeFormat HeldFormat = CompositeFormat.Parse(ValidationMessages.FunctionAlreadyHeld);

    private static readonly CompositeFormat HeldInSectionFormat =
        CompositeFormat.Parse(ValidationMessages.FunctionAlreadyHeldInSection);

    public RolesViewModel(IIncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _personnel = masterData.Personnel;
        _roles = masterData.Roles;
        IsReadOnly = session.IsReadOnly;
        RoleOptions = _roles.Select(r => r.Name).ToArray();
        CallSignOptions = masterData.RadioCallSigns;
        PersonOptions = masterData.Personnel.Select(p => p.DisplayName).ToArray();
        Roles = new ObservableCollection<RoleAssignmentRow>();
        RefreshRoles();
        _session.Changed += RefreshRoles;
    }

    public void Dispose() => _session.Changed -= RefreshRoles;

    // Brings the rows in line with the incident on any change -- this device's edit, or (when
    // joined) another's -- by id and in place rather than Clear()+re-add, which dropped the
    // selection and focus in the grid on every change (#294).
    private void RefreshRoles()
    {
        var kept = _all.ToDictionary(r => r.Id);
        _all.Clear();
        foreach (var assignment in _session.Incident.Roles)
        {
            if (kept.TryGetValue(assignment.Id, out var row))
            {
                row.Update(assignment);
                _all.Add(row);
            }
            else
            {
                _all.Add(CreateRow(assignment));
            }
        }

        MarkDuplicates(); // after every row exists — see the comment on it
        ApplyFilter();

        // A handover changes who holds what without touching the form, so a conflict computed from
        // the rows has to be re-raised here or the hint would name the person who just left (#470).
        OnPropertyChanged(nameof(ConflictingRow));
        OnPropertyChanged(nameof(ConflictHint));
    }

    // The add form refuses a second holder of a unique Funktion, but it cannot refuse one that is
    // already recorded: an older Einsatzdatei, an imported .fwincident or a joined device running
    // other Stammdaten can each bring one, and an Einsatz has to keep working. So such a row is
    // marked instead — on both rows, since either could be the one to hand over, and a marker that
    // picked a winner would be naming a culprit in a record of what happened.
    //
    // The second pass is a correctness requirement, not an optimisation: RefreshRoles builds the
    // rows through CreateRow while _all is still filling, so a row's partner is not in the list yet
    // when the row itself is created. O(n²) over _all costs nothing — an Einsatz holds tens of
    // assignments, not thousands.
    private void MarkDuplicates()
    {
        foreach (var row in _all)
        {
            // Resolved once per row. A Funktion the Stammdaten do not list, or one they leave
            // Multiple, carries no mode and can never be a duplicate — the tolerance
            // StammdatenCatalogue exists for, and the same case IsNewRoleUnknown hints about.
            // Written false rather than skipped, so a row that was marked in the previous refresh
            // is visibly cleared instead of merely having been replaced by a clean row.
            if (StammdatenCatalogue.Find(row.Role, _roles) is not { Uniqueness: > RoleUniqueness.Multiple } mode)
            {
                row.IsDuplicate = false;
                continue;
            }

            // The same question FindRunningHolder asks, and it must branch the same way (Ruling 16):
            // a Funktion unique per Abschnitt is one holder per Abschnitt, one unique per Einsatz one
            // for the whole Einsatz whatever Abschnitt either sits in. If the two call sites drifted,
            // the grid would mark a row the add form does not flag, or the reverse.
            //
            // Liveness is tested on the row as well as on its partner, and a handover is the state
            // that demands it: a handed-over Funktion always leaves an ended row beside a running
            // one, so a partner-only test would mark the app's own history -- and put a warning
            // where there is no ÜBERTRAGEN button to answer it with.
            row.IsDuplicate = row.IsRunning && _all.Any(other =>
                other.Id != row.Id
                && other.IsRunning
                && string.Equals(other.Role?.Trim(), row.Role?.Trim(), StringComparison.OrdinalIgnoreCase)
                && (mode.Uniqueness != RoleUniqueness.UniquePerSection
                    || SameSection(other.Section, row.Section)));
        }
    }

    // Two absent Abschnitte are one bucket, one absent and one named are not. Same rule as
    // Incident.FindRunningRoleHolder, which is what the add form asks and which has to agree with
    // this: the domain keeps the normalisation in a private NormalizeSection, so the equality is
    // spelled here rather than a second implementation of "blank ≡ blank" living next to it.
    private static bool SameSection(string? left, string? right) =>
        string.Equals(
            left?.Trim() ?? string.Empty,
            right?.Trim() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    // _all already holds the reconciled rows, so the visible subset is reconciled from those
    // same instances: a filter toggle or a handover inserts and removes rows, never resets the grid.
    private void ApplyFilter() =>
        RowReconciler.Reconcile(
            Roles,
            _all.Where(r => ShowAllRoles || r.IsRunning).ToList(),
            r => r.Id,
            r => r.Id,
            r => r,
            (_, _) => { });

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
    [NotifyPropertyChangedFor(nameof(ConflictingRow))]
    [NotifyPropertyChangedFor(nameof(ConflictHint))]
    private string _newRole = string.Empty;

    /// <summary>
    /// Whether the typed Funktion is absent from the Stammdaten — drives a hint, never a block.
    /// Funktion is deliberately free text (an ad-hoc or mutual-aid role must stay enterable), so
    /// this only points out that the entry will not match the rest; the assignment is made either
    /// way. False while no Funktionen are configured at all, where it would flag everything.
    /// </summary>
    public bool IsNewRoleUnknown => StammdatenCatalogue.IsUnknown(NewRole, RoleOptions);

    /// <summary>
    /// How often the typed Funktion may be held, per the Stammdaten. A Funktion they do not list
    /// carries no mode, so it reads as <see cref="RoleUniqueness.Multiple"/> and nothing is refused
    /// for it — the tolerance <see cref="StammdatenCatalogue"/> exists for, and the same case
    /// <see cref="IsNewRoleUnknown"/> flags for the operator.
    /// </summary>
    private RoleUniqueness NewRoleUniqueness =>
        StammdatenCatalogue.Find(NewRole, _roles)?.Uniqueness ?? RoleUniqueness.Multiple;

    /// <summary>
    /// The running holder of the typed Funktion when the Stammdaten say it may be held only once —
    /// the row a press on ZUWEISEN opens the Übergabe on. Null while the Funktion may be held by
    /// anyone, and while nothing is typed. It is a row rather than the domain's assignment because
    /// the handover panel and the grid both work on rows.
    /// </summary>
    public RoleAssignmentRow? ConflictingRow
    {
        get
        {
            // The blank NewRole needs no guard of its own: StammdatenCatalogue.Find already returns
            // null for one, so NewRoleUniqueness reads Multiple and this half fires. Were that ever
            // to change, FindRunningRoleHolder's own blank-role guard -- pinned by
            // Find_running_role_holder_returns_null_for_a_blank_funktions_role -- still yields no
            // holder below, so the property cannot report a conflict on an empty field either way.
            if (NewRoleUniqueness == RoleUniqueness.Multiple)
            {
                return null;
            }

            var holder = FindRunningHolder();
            return holder is null ? null : _all.FirstOrDefault(r => r.Id == holder.Id);
        }
    }

    // Which question to ask is the whole rule (#470): a Funktion unique per Abschnitt is one holder
    // per Abschnitt, a Funktion unique per Einsatz one holder for the Einsatz whatever Abschnitt
    // either of them is typed into. Asking the section-scoped one for a per-Einsatz Funktion would
    // leave a second EL in another Abschnitt unflagged, so the duplicate marking the grid carries
    // (MarkDuplicates) has to branch the same way.
    private RoleAssignment? FindRunningHolder() =>
        NewRoleUniqueness == RoleUniqueness.UniquePerSection
            ? _session.Incident.FindRunningRoleHolder(NewRole, NewSection)
            : _session.Incident.FindRunningRoleHolderInAnySection(NewRole);

    /// <summary>What to say about the conflict, or null when there is none: who holds the typed
    /// Funktion, so the operator can see that the press hands over rather than assigns.</summary>
    public string? ConflictHint
    {
        get
        {
            if (ConflictingRow is not { } row)
            {
                return null;
            }

            // A holder with no Abschnitt names none, so the sentence drops that half rather than
            // reading "in Abschnitt  bereits besetzt". Keyed on the holder's Abschnitt, which the
            // section-scoped lookup has already matched against the typed one.
            return string.IsNullOrWhiteSpace(row.Section)
                ? string.Format(De, HeldFormat, row.Role, row.PersonName)
                : string.Format(De, HeldInSectionFormat, row.Role, row.Section, row.PersonName);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewPersonNameError))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    private string _newPersonName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConflictingRow))]
    [NotifyPropertyChangedFor(nameof(ConflictHint))]
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

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isNarrow;

    /// <summary>
    /// Whether the narrow layout's assign form is open. Five fields across ~750px; a phone stacks
    /// them and only while a Funktion is actually being assigned.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isComposerOpen;

    /// <summary>Whether the dock is on screen: always when wide, only while composing on a phone.</summary>
    public bool ShowComposer => !IsNarrow || IsComposerOpen;

    /// <summary>The phone's "assign a Funktion" affordance, shown exactly when the dock is not.</summary>
    public bool ShowComposerButton => IsNarrow && !IsComposerOpen;

    [RelayCommand(CanExecute = nameof(CanAddRole))]
    private void OpenComposer() => IsComposerOpen = true;

    [RelayCommand]
    private void CloseComposer()
    {
        IsComposerOpen = false;
        ShowAddErrors(false); // a dismissed form must not reopen still complaining
    }

    [RelayCommand(CanExecute = nameof(CanAddRole))]
    private void AddRole()
    {
        if (!ValidateAdd())
        {
            return;
        }

        // A Funktion the Stammdaten marked unique is already held, so rather than refusing with a
        // dead end, point the operator at the handover: it is the only thing that legitimately
        // replaces a running holder, and it is one click away in the grid. The form keeps its
        // values, so cancelling the panel returns here as it was (#470).
        if (ConflictingRow is { } taken)
        {
            BeginTransfer(taken);
            TransferPersonName = NewPersonName;
            TransferCallSign = NewCallSign;
            TransferPhone = NewPhone;
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

        // Reached only on success, so this is where the phone's form closes again.
        IsComposerOpen = false;
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
        _session.EditRolePhone(row.Id, phone); // Changed → RefreshRoles updates the row
        _onChanged();
    }
}
