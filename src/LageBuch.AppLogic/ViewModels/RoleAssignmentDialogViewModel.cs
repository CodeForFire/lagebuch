using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The form for a Funktion that is already held: corrects who holds it and how they are reached
/// (<see cref="RoleAssignmentDialogMode.Edit"/>), or hands it over to someone else
/// (<see cref="RoleAssignmentDialogMode.Transfer"/>). Opened from a Führungsunterstützung on the
/// Führung board and from BEARBEITEN in FUNKTIONEN. Same contract as
/// <see cref="LeadershipPersonDialogViewModel"/>: buffered until the primary button, a discard
/// question on ABBRECHEN after typing (#652), and <see cref="Closed"/> on every outcome.
/// </summary>
/// <remarks>
/// The Abschnitt is offered only for a Funktion nobody placed on the board; a placed one is moved
/// there, and the domain refuses the move here as well.
/// </remarks>
public sealed partial class RoleAssignmentDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action? _onChanged;
    private readonly IReadOnlyList<Person> _personnel;
    private readonly IReadOnlyList<Role> _roles;
    private readonly Guid _assignmentId;
    private readonly RoleAssignment _seed;

    // What the roster last wrote into Funkrufname and Handynummer (#656).
    private string? _prefilledCallSign;
    private string? _prefilledPhone;

    /// <param name="session">The incident the Funktion is recorded in.</param>
    /// <param name="masterData">The Personal roster and the Funkrufnamen offered as suggestions.</param>
    /// <param name="assignmentId">The running assignment to correct or hand over.</param>
    /// <param name="mode">Whether the save corrects or hands over.</param>
    /// <param name="onChanged">Tells the host something was written.</param>
    public RoleAssignmentDialogViewModel(
        IIncidentSession session,
        MasterDataSet masterData,
        Guid assignmentId,
        RoleAssignmentDialogMode mode,
        Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _onChanged = onChanged;
        _personnel = masterData.Personnel;
        _roles = masterData.Roles;
        _assignmentId = assignmentId;
        _seed = session.Incident.Roles.FirstOrDefault(r => r.Id == assignmentId)
            ?? throw new ArgumentException("Funktionszuweisung nicht gefunden.", nameof(assignmentId));
        Mode = mode;
        PersonOptions = [.. masterData.Personnel.Select(p => p.DisplayName)];
        CallSignOptions = masterData.RadioCallSigns;

        (Title, PrimaryText) = mode == RoleAssignmentDialogMode.Edit
            ? ($"{_seed.Role} bearbeiten", "SPEICHERN")
            : ($"Übergabe {_seed.Role}: {_seed.PersonName}", "ÜBERTRAGEN");

        // A correction starts from what is recorded; a handover names someone new.
        if (mode == RoleAssignmentDialogMode.Edit)
        {
            _personName = _seed.PersonName;
            _callSign = _seed.CallSign;
            _phone = _seed.Phone;
            _section = _seed.Section;
        }

        RefreshGone();
        _session.Changed += OnSessionChanged;
    }

    public event EventHandler? Closed;

    public RoleAssignmentDialogMode Mode { get; }

    public string Title { get; }

    /// <summary>SPEICHERN for a correction, ÜBERTRAGEN for a handover.</summary>
    public string PrimaryText { get; }

    /// <summary>The Funktion, which neither mode changes.</summary>
    public string Role => _seed.Role;

    /// <summary>The Personal roster's names; empty without a roster, the box stays free text.</summary>
    public IReadOnlyList<string> PersonOptions { get; }

    /// <summary>The Funkrufnamen of the Stammdaten, as suggestions.</summary>
    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>
    /// Whether the ABSCHNITT field is shown: only when correcting a Funktion the board has not
    /// placed. A handover keeps the place, and a placed Funktion is moved on the board.
    /// </summary>
    public bool IsSectionEditable =>
        Mode == RoleAssignmentDialogMode.Edit && _seed.NodeId is null && _seed.Slot == RoleSlot.None;

    [ObservableProperty]
    private string _personName = string.Empty;

    [ObservableProperty]
    private string? _callSign;

    [ObservableProperty]
    private string? _phone;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _section;

    /// <summary>"Name fehlt." once a save found the name blank; null otherwise.</summary>
    [ObservableProperty]
    private string? _personError;

    /// <summary>Who already holds this Funktion in the Abschnitt typed, when the Stammdaten allow one per Abschnitt; blocks saving.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _sectionError;

    /// <summary>Why the last save was refused, or that the Funktion has since ended elsewhere; null otherwise.</summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>Set while ABBRECHEN asks whether typed details may go (#652); the primary button waits for the answer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDiscardConfirmOpen;

    // True once the assignment ended or vanished under the dialog (a peer's handover).
    private bool _isGone;

    /// <summary>Whether anything was typed since the dialog opened.</summary>
    public bool HasChanges => Mode == RoleAssignmentDialogMode.Edit
        ? !SameText(PersonName, _seed.PersonName) || !SameText(CallSign, _seed.CallSign)
            || !SameText(Phone, _seed.Phone) || !SameText(Section, _seed.Section)
        : !string.IsNullOrWhiteSpace(PersonName) || !string.IsNullOrWhiteSpace(CallSign)
            || !string.IsNullOrWhiteSpace(Phone);

    private bool CanSave => !_session.IsReadOnly && !IsDiscardConfirmOpen && !_isGone && SectionError is null;

    partial void OnPersonNameChanged(string value)
    {
        if (PersonError is not null && !string.IsNullOrWhiteSpace(value))
        {
            PersonError = null;
        }

        var person = RosterLookup.Find(_personnel, value);
        _prefilledPhone = RosterLookup.Refill(Phone, _prefilledPhone, person?.Phone, v => Phone = v);
        _prefilledCallSign = RosterLookup.Refill(CallSign, _prefilledCallSign, person?.CallSign, v => CallSign = v);
    }

    partial void OnSectionChanged(string? value) => RefreshSectionError();

    /// <summary>
    /// Stops following the session. <see cref="Closed"/> does this itself; a host that drops the
    /// dialog without closing it (the incident closed under the overlay) calls it.
    /// </summary>
    public void Detach() => _session.Changed -= OnSessionChanged;

    private void OnSessionChanged()
    {
        RefreshGone();
        RefreshSectionError();
    }

    // A peer may hand the Funktion over while this is open; the save would then be refused, so the
    // dialog says so at once and keeps what was typed visible.
    private void RefreshGone()
    {
        _isGone = !_session.Incident.Roles.Any(r => r.Id == _assignmentId && r.To is null);
        if (_isGone)
        {
            SaveError = "Diese Funktion wurde inzwischen übergeben.";
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSectionError()
    {
        SectionError = IsSectionEditable && SectionHolder() is { } holder
            ? $"„{holder.Role}“ ist in {holder.Section} bereits mit {holder.PersonName} besetzt."
            : null;
    }

    // Another running holder of this Funktion in the Abschnitt typed, when the Stammdaten allow one
    // per Abschnitt. Blank is a place of its own, as everywhere else (Incident.FindRunningRoleHolder).
    private RoleAssignment? SectionHolder()
    {
        if (StammdatenCatalogue.Find(_seed.Role, _roles)?.Uniqueness != RoleUniqueness.UniquePerSection
            || SameText(Section, _seed.Section))
        {
            return null;
        }

        var holder = _session.Incident.FindRunningRoleHolder(_seed.Role, Normalize(Section));
        return holder is not null && holder.Id != _assignmentId ? holder : null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveError = null;
        PersonError = string.IsNullOrWhiteSpace(PersonName) ? "Name fehlt." : null;
        if (PersonError is not null)
        {
            return;
        }

        var person = PersonName.Trim();
        var callSign = Normalize(CallSign);
        var phone = Normalize(Phone);
        try
        {
            if (Mode == RoleAssignmentDialogMode.Edit)
            {
                var section = IsSectionEditable ? Normalize(Section) : _seed.Section;
                _session.EditRoleAssignment(_assignmentId, person, callSign, phone, section);
            }
            else
            {
                _session.TransferRole(_assignmentId, person, callSign, phone);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            SaveError = DomainRefusal.Message(ex);
            return;
        }

        _onChanged?.Invoke();
        Close();
    }

    /// <summary>ABBRECHEN, and Esc: closes at once when nothing was typed, asks first otherwise.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (HasChanges)
        {
            IsDiscardConfirmOpen = true;
            return;
        }

        Close();
    }

    [RelayCommand]
    private void ConfirmDiscard()
    {
        IsDiscardConfirmOpen = false;
        Close();
    }

    [RelayCommand]
    private void CancelDiscard() => IsDiscardConfirmOpen = false;

    private void Close()
    {
        Detach();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameText(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
