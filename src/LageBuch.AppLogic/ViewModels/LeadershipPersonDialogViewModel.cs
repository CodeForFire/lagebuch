using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Führung board's person form: sets who leads a node or the whole Einsatz (Leiter), who carries
/// its messages (Melder), adds a Führungsassistent or Fachberater to the Einsatzleitung, or another
/// Funktion to a node. Every one of them is an ordinary Funktion, shown in FUNKTIONEN too, carrying
/// the target's node and slot. Same contract as <see cref="CommandNodeDialogViewModel"/>: buffered
/// until the primary button, a discard question on ABBRECHEN after typing (#652), and
/// <see cref="Closed"/> on every outcome.
/// </summary>
/// <remarks>
/// Who is replaced follows FUNKTIONEN (#470): a Leiter or Melder post that is already held is handed
/// over, and so is a Funktion the Stammdaten mark unique whose holder is running. The holder is first
/// placed where the dialog was opened for, because a Funktion recorded in FUNKTIONEN (or before the
/// board) has no node and no slot, and a handover keeps both: without the placement the clicked post
/// would stay empty. When the holder is the person named, the placement is the whole action.
/// Only a Leiter or Melder post takes a unique Funktion's holder over: a Führungsassistent,
/// Fachberater or other Funktion added beside others must not pull the running EL out of its post,
/// so there the conflict is a field error that points at the handover where the holder sits.
/// </remarks>
public sealed partial class LeadershipPersonDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action? _onChanged;
    private readonly IReadOnlyList<Role> _roles;
    private readonly IReadOnlyList<Person> _personnel;
    private readonly IReadOnlyList<string> _spellings;
    private readonly Guid? _nodeId;
    private readonly RoleSlot _slot;
    private string _seededRole;

    // Whether a save found the Funktion blank; the message then stands until a Funktion is typed.
    private bool _blankRoleShown;

    // What the roster last wrote into Funkrufname and Handynummer (#656).
    private string? _prefilledCallSign;
    private string? _prefilledPhone;

    /// <param name="session">The incident the person goes into.</param>
    /// <param name="clock">Stamps Von on a new assignment, as FUNKTIONEN does.</param>
    /// <param name="masterData">The Funktionen and the Personal roster offered as suggestions.</param>
    /// <param name="nodeId">The node the person goes into; null for the Einsatzleitung itself.</param>
    /// <param name="slot">The post on the sheet; recorded as it is, whatever Funktion is typed.</param>
    /// <param name="onChanged">Tells the host something was written.</param>
    public LeadershipPersonDialogViewModel(
        IIncidentSession session,
        IClock clock,
        MasterDataSet masterData,
        Guid? nodeId,
        RoleSlot slot,
        Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _roles = masterData.Roles;
        _personnel = masterData.Personnel;
        _nodeId = nodeId;
        _slot = slot;

        RoleOptions = BuildRoleOptions(masterData.Roles, slot);
        _spellings = [.. RoleOptions.Concat(masterData.Roles.Select(r => r.Name))];
        PersonOptions = [.. masterData.Personnel.Select(p => p.DisplayName)];
        CallSignOptions = masterData.RadioCallSigns;

        var node = session.Incident.CommandNodes.FirstOrDefault(n => n.Id == nodeId);
        var label = node?.Name ?? string.Empty;
        (Title, PrimaryText) = (slot, nodeId) switch
        {
            (RoleSlot.Leiter, null) => ($"{LeiterLabels.Of(null)} festlegen", "FESTLEGEN"),
            (RoleSlot.Leiter, _) => ($"{LeiterLabels.Of(node?.Kind)} festlegen – {label}", "FESTLEGEN"),
            (RoleSlot.Melder, null) => ("Melder festlegen – Einsatzleitung", "FESTLEGEN"),
            (RoleSlot.Melder, _) => ($"Melder festlegen – {label}", "FESTLEGEN"),
            (RoleSlot.Fuehrungsassistent, _) => ("Führungsunterstützung hinzufügen", "HINZUFÜGEN"),
            (RoleSlot.Fachberater, _) => ("Fachberater hinzufügen", "HINZUFÜGEN"),
            _ => ($"Funktion hinzufügen – {label}", "HINZUFÜGEN"),
        };

        // A post already held is handed over in its own Funktion, so that is what the field shows.
        _seededRole = PostHolder()?.Role ?? Preselect(masterData.Roles, slot, nodeId);
        _role = _seededRole;
        _session.Changed += OnSessionChanged;
    }

    public event EventHandler? Closed;

    public string Title { get; }

    /// <summary>FESTLEGEN for a Leiter or Melder post, HINZUFÜGEN for everything that may be held by several.</summary>
    public string PrimaryText { get; }

    /// <summary>The Stammdaten's Funktionen for this post, as suggestions; a Funktion they lack stays enterable.</summary>
    public IReadOnlyList<string> RoleOptions { get; }

    /// <summary>The Personal roster's names; empty without a roster, the box stays free text.</summary>
    public IReadOnlyList<string> PersonOptions { get; }

    /// <summary>The Funkrufnamen of the Stammdaten, as suggestions, as in FUNKTIONEN.</summary>
    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>
    /// False while the post is held: a handover keeps the holder's Funktion, so the field shows it
    /// and cannot promise another.
    /// </summary>
    public bool IsRoleEditable => PostHolder() is null;

    /// <summary>
    /// False while the person named already holds the Funktion: placing or leaving them is no
    /// handover, so a Funkrufname or Handynummer typed now would be dropped. The fields then show the
    /// holder's own values and are read-only (Ruling P10).
    /// </summary>
    [ObservableProperty]
    private bool _isContactEditable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandoverText))]
    private string _role;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandoverText))]
    private string _personName = string.Empty;

    [ObservableProperty]
    private string? _callSign;

    [ObservableProperty]
    private string? _phone;

    /// <summary>
    /// "Funktion fehlt." once a save found the Funktion blank, or, outside a Leiter or Melder post,
    /// who already holds the unique Funktion typed; null otherwise. The latter blocks saving.
    /// </summary>
    [ObservableProperty]
    private string? _roleError;

    /// <summary>"Name fehlt." once a save found the name blank; null otherwise.</summary>
    [ObservableProperty]
    private string? _personError;

    /// <summary>Why the last save was refused, in German; null otherwise.</summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>Set while ABBRECHEN asks whether typed details may go (#652); the primary button waits for the answer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDiscardConfirmOpen;

    /// <summary>
    /// What the save does to an existing holder, in German, or null when it assigns a new Funktion:
    /// the running holder of the Leiter or Melder post, or the holder of a Funktion the Stammdaten mark
    /// unique, is moved here and/or handed over to the person named.
    /// </summary>
    public string? HandoverText
    {
        get
        {
            var (holder, isPost) = FindHolder(RoleToSave);
            if (holder is null)
            {
                return null;
            }

            return (IsInPlace(holder), IsSamePerson(holder)) switch
            {
                (false, true) => $"„{holder.Role}“ ({holder.PersonName}) wird hierher übernommen.",
                (true, true) => $"„{holder.Role}“ hat bereits {holder.PersonName}.",
                _ when isPost => $"Übergabe: {holder.PersonName} → neu",
                (false, false) => $"„{holder.Role}“ hat bereits {holder.PersonName} – wird hierher übergeben.",
                _ => $"„{holder.Role}“ hat bereits {holder.PersonName} – wird übergeben.",
            };
        }
    }

    /// <summary>Whether anything was typed since the dialog opened.</summary>
    public bool HasChanges =>
        !string.Equals(Role?.Trim(), _seededRole, StringComparison.Ordinal)
        || !string.IsNullOrWhiteSpace(PersonName)
        || !string.IsNullOrWhiteSpace(CallSign)
        || !string.IsNullOrWhiteSpace(Phone);

    private bool CanSave => !_session.IsReadOnly && !IsDiscardConfirmOpen && ConflictHolder() is null;

    // Leiter and Melder are single-holder posts: taking the holder over is what was asked for.
    private bool TakesOverHolders => _slot is RoleSlot.Leiter or RoleSlot.Melder;

    // The Funktion in the catalogue's spelling when it matches one apart from case or spacing, so
    // "s2 lage" does not become a second Funktion beside "S2 Lage"; otherwise as typed.
    private string RoleToSave => StammdatenCatalogue.Normalize(Role, _spellings) ?? string.Empty;

    private CommandNode? TargetNode =>
        _nodeId is { } id ? _session.Incident.CommandNodes.FirstOrDefault(n => n.Id == id) : null;

    partial void OnRoleChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _blankRoleShown = false;
        }

        RefreshRoleError();
        RefreshContactLock();
    }

    // Shows the holder's contact details and locks the fields while the holder is the person named,
    // and gives the fields back, cleared and refilled from the roster, once someone else is named.
    private void RefreshContactLock()
    {
        var holder = FindHolder(RoleToSave).Holder;
        var locked = false;
        if (holder is not null && IsSamePerson(holder))
        {
            locked = true;
            CallSign = holder.CallSign;
            Phone = holder.Phone;
            _prefilledCallSign = CallSign;
            _prefilledPhone = Phone;
        }
        else if (!IsContactEditable)
        {
            CallSign = null;
            Phone = null;
            _prefilledCallSign = null;
            _prefilledPhone = null;
            var person = RosterLookup.Find(_personnel, PersonName);
            _prefilledPhone = RosterLookup.Refill(Phone, _prefilledPhone, person?.Phone, v => Phone = v);
            _prefilledCallSign = RosterLookup.Refill(CallSign, _prefilledCallSign, person?.CallSign, v => CallSign = v);
        }

        IsContactEditable = !locked;
    }

    private void RefreshRoleError()
    {
        RoleError = ConflictHolder() is { } holder
            ? $"„{holder.Role}“ ist bereits mit {holder.PersonName} besetzt – dort übergeben."
            : _blankRoleShown && IsRoleEditable && string.IsNullOrWhiteSpace(Role) ? "Funktion fehlt." : null;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnPersonNameChanged(string value)
    {
        if (PersonError is not null && !string.IsNullOrWhiteSpace(value))
        {
            PersonError = null;
        }

        var person = RosterLookup.Find(_personnel, value);
        _prefilledPhone = RosterLookup.Refill(Phone, _prefilledPhone, person?.Phone, v => Phone = v);
        _prefilledCallSign = RosterLookup.Refill(CallSign, _prefilledCallSign, person?.CallSign, v => CallSign = v);
        RefreshContactLock();
    }

    /// <summary>
    /// Stops following the session. <see cref="Closed"/> does this itself; a host that drops the
    /// dialog without closing it (the incident closed under the overlay) calls it.
    /// </summary>
    public void Detach() => _session.Changed -= OnSessionChanged;

    // A peer's handover or assignment changes who would be replaced; the hint must not name someone
    // who already left (#470). The save itself asks again, so this only keeps the text honest. A post
    // a peer just filled turns the dialog into its handover, in that holder's Funktion.
    private void OnSessionChanged()
    {
        if (PostHolder() is { } post && !string.Equals(Role, post.Role, StringComparison.Ordinal))
        {
            _seededRole = post.Role;
            Role = post.Role;
        }

        OnPropertyChanged(nameof(IsRoleEditable));
        OnPropertyChanged(nameof(HandoverText));
        RefreshRoleError();
        RefreshContactLock();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveError = null;
        _blankRoleShown = string.IsNullOrWhiteSpace(Role);
        RefreshRoleError();
        PersonError = string.IsNullOrWhiteSpace(PersonName) ? "Name fehlt." : null;
        if (RoleError is not null || PersonError is not null)
        {
            return;
        }

        if (_nodeId is not null && TargetNode is not { IsLive: true })
        {
            SaveError = "Dieser Abschnitt wurde inzwischen aufgelöst.";
            return;
        }

        var role = RoleToSave;
        var person = PersonName.Trim();
        var callSign = Normalize(CallSign);
        var phone = Normalize(Phone);
        var wrote = false;
        try
        {
            if (FindHolder(role).Holder is { } holder)
            {
                if (!IsInPlace(holder))
                {
                    _session.PlaceRoleAssignment(holder.Id, _nodeId, _slot);
                    wrote = true;
                }

                if (!IsSamePerson(holder))
                {
                    _session.TransferRole(holder.Id, person, callSign, phone);
                    wrote = true;
                }
            }
            else
            {
                _session.AssignRole(role, person, callSign, from: _clock.Now, phone: phone, nodeId: _nodeId, slot: _slot);
                wrote = true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            // A placement that went through before the handover was refused is still a change.
            if (wrote)
            {
                _onChanged?.Invoke();
            }

            SaveError = DomainRefusal.Message(ex);
            return;
        }

        if (wrote)
        {
            _onChanged?.Invoke();
        }

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

    // The running holder of the Leiter or Melder post this dialog was opened for; null for any other
    // slot, which several may hold.
    private RoleAssignment? PostHolder() =>
        _slot is RoleSlot.Leiter or RoleSlot.Melder
            ? _session.Incident.Roles.FirstOrDefault(r => r.To is null && r.Slot == _slot && IsInTarget(r))
            : null;

    // The running assignment this save would hand over from, and whether it is the post itself
    // (a Leiter or Melder already set) rather than a Funktion the Stammdaten allow only once.
    private (RoleAssignment? Holder, bool IsPost) FindHolder(string role)
    {
        if (PostHolder() is { } post)
        {
            return (post, true);
        }

        return (TakesOverHolders ? UniqueHolder(role) : null, false);
    }

    // Outside a Leiter or Melder post, the running holder of the unique Funktion typed: a conflict
    // to report, never a holder to move.
    private RoleAssignment? ConflictHolder() => TakesOverHolders ? null : UniqueHolder(RoleToSave);

    // The running holder of a Funktion the Stammdaten allow once per Einsatz, or once per Abschnitt
    // (node-aware, so a same-named Unterabschnitt elsewhere is no conflict); null otherwise.
    private RoleAssignment? UniqueHolder(string role)
    {
        var incident = _session.Incident;
        return StammdatenCatalogue.Find(role, _roles)?.Uniqueness switch
        {
            RoleUniqueness.UniquePerIncident => incident.FindRunningRoleHolderInAnySection(role),
            RoleUniqueness.UniquePerSection => incident.FindRunningRoleHolder(role, TargetNode?.Name, _nodeId),
            _ => null,
        };
    }

    // The Einsatzleitung itself is the assignment with no node and no free-text Abschnitt, the same
    // test ReportingLine uses for its Leiter.
    private bool IsInTarget(RoleAssignment r) =>
        _nodeId is { } id ? r.NodeId == id : r.NodeId is null && string.IsNullOrWhiteSpace(r.Section);

    // Exactly where the dialog puts people: the target and the slot. Anything else is placed first.
    private bool IsInPlace(RoleAssignment r) => r.Slot == _slot && IsInTarget(r);

    private bool IsSamePerson(RoleAssignment r) =>
        string.Equals(r.PersonName, PersonName?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string[] BuildRoleOptions(IReadOnlyList<Role> roles, RoleSlot slot)
    {
        // A slot no Funktion carries offers them all: Stammdaten from before the Führungstafel
        // slot existed set none, and an empty box would suggest nothing at all.
        var inSlot = roles.Where(r => r.Slot == slot).ToList();
        var options = (slot == RoleSlot.None || inSlot.Count == 0 ? roles : inSlot).Select(r => r.Name).ToList();
        if (slot == RoleSlot.Fachberater)
        {
            options.AddRange(FachberaterSuggestions.Roles.Where(
                s => !options.Contains(s, StringComparer.OrdinalIgnoreCase)));
        }

        return [.. options];
    }

    // A Leiter post asks for the Funktion that is unique where it goes: once per Abschnitt in a node,
    // once per Einsatz at the Einsatzleitung. Posts that several may hold start blank.
    private static string Preselect(IReadOnlyList<Role> roles, RoleSlot slot, Guid? nodeId)
    {
        if (slot is not (RoleSlot.Leiter or RoleSlot.Melder))
        {
            return string.Empty;
        }

        var inSlot = roles.Where(r => r.Slot == slot).ToList();
        var unique = slot == RoleSlot.Leiter
            ? inSlot.Find(r => r.Uniqueness == (nodeId is null ? RoleUniqueness.UniquePerIncident : RoleUniqueness.UniquePerSection))
            : null;
        return (unique ?? inSlot.FirstOrDefault())?.Name ?? string.Empty;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
