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
/// over, and so is a Funktion the Stammdaten mark unique whose holder is running. A handover keeps
/// the holder's Funktion, node and slot, as <see cref="IIncidentSession.TransferRole"/> always does.
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
    private readonly string _seededRole;

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

        var label = session.Incident.CommandNodes.FirstOrDefault(n => n.Id == nodeId)?.Name ?? string.Empty;
        (Title, PrimaryText) = (slot, nodeId) switch
        {
            (RoleSlot.Leiter, null) => ("Einsatzleiter festlegen", "FESTLEGEN"),
            (RoleSlot.Leiter, _) => ($"Leiter festlegen – {label}", "FESTLEGEN"),
            (RoleSlot.Melder, _) => ($"Melder festlegen – {label}", "FESTLEGEN"),
            (RoleSlot.Fuehrungsassistent, _) => ("Führungsassistenz hinzufügen", "HINZUFÜGEN"),
            (RoleSlot.Fachberater, _) => ("Fachberater hinzufügen", "HINZUFÜGEN"),
            _ => ($"Funktion hinzufügen – {label}", "HINZUFÜGEN"),
        };

        _seededRole = Preselect(masterData.Roles, slot, nodeId);
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandoverText))]
    private string _role;

    [ObservableProperty]
    private string _personName = string.Empty;

    [ObservableProperty]
    private string? _callSign;

    [ObservableProperty]
    private string? _phone;

    /// <summary>"Funktion fehlt." once a save found the Funktion blank; null otherwise.</summary>
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
    /// Who the save hands over from, in German, or null when it assigns a new Funktion: the running
    /// holder of the Leiter or Melder post, or the holder of a Funktion the Stammdaten mark unique.
    /// </summary>
    public string? HandoverText => FindHolder(RoleToSave) switch
    {
        (null, _) => null,
        ({ } holder, true) => $"Übergabe: {holder.PersonName} → neu",
        ({ } holder, false) => $"„{holder.Role}“ hat bereits {holder.PersonName} – wird übergeben.",
    };

    /// <summary>Whether anything was typed since the dialog opened.</summary>
    public bool HasChanges =>
        !string.Equals(Role?.Trim(), _seededRole, StringComparison.Ordinal)
        || !string.IsNullOrWhiteSpace(PersonName)
        || !string.IsNullOrWhiteSpace(CallSign)
        || !string.IsNullOrWhiteSpace(Phone);

    private bool CanSave => !_session.IsReadOnly && !IsDiscardConfirmOpen;

    // The Funktion in the catalogue's spelling when it matches one apart from case or spacing, so
    // "s2 lage" does not become a second Funktion beside "S2 Lage"; otherwise as typed.
    private string RoleToSave => StammdatenCatalogue.Normalize(Role, _spellings) ?? string.Empty;

    private CommandNode? TargetNode =>
        _nodeId is { } id ? _session.Incident.CommandNodes.FirstOrDefault(n => n.Id == id) : null;

    partial void OnRoleChanged(string value)
    {
        if (RoleError is not null && !string.IsNullOrWhiteSpace(value))
        {
            RoleError = null;
        }
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
    }

    // A peer's handover or assignment changes who would be replaced; the hint must not name someone
    // who already left (#470). The save itself asks again, so this only keeps the text honest.
    private void OnSessionChanged() => OnPropertyChanged(nameof(HandoverText));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveError = null;
        RoleError = string.IsNullOrWhiteSpace(Role) ? "Funktion fehlt." : null;
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
        var callSign = Normalize(CallSign);
        var phone = Normalize(Phone);
        try
        {
            if (FindHolder(role).Holder is { } holder)
            {
                _session.TransferRole(holder.Id, PersonName.Trim(), callSign, phone);
            }
            else
            {
                _session.AssignRole(
                    role, PersonName.Trim(), callSign, from: _clock.Now, phone: phone, nodeId: _nodeId, slot: _slot);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            SaveError = $"Nicht möglich: {ex.Message}";
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
        _session.Changed -= OnSessionChanged;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    // The running assignment this save would hand over from, and whether it is the post itself
    // (a Leiter or Melder already set) rather than a Funktion the Stammdaten allow only once.
    private (RoleAssignment? Holder, bool IsPost) FindHolder(string role)
    {
        var incident = _session.Incident;
        if (_slot is RoleSlot.Leiter or RoleSlot.Melder
            && incident.Roles.FirstOrDefault(r => r.To is null && r.Slot == _slot && IsInTarget(r)) is { } post)
        {
            return (post, true);
        }

        var holder = StammdatenCatalogue.Find(role, _roles)?.Uniqueness switch
        {
            RoleUniqueness.UniquePerIncident => incident.FindRunningRoleHolderInAnySection(role),
            RoleUniqueness.UniquePerSection => incident.FindRunningRoleHolder(role, TargetNode?.Name, _nodeId),
            _ => null,
        };
        return (holder, false);
    }

    // The Einsatzleitung itself is the assignment with no node and no free-text Abschnitt, the same
    // test ReportingLine uses for its Leiter.
    private bool IsInTarget(RoleAssignment r) =>
        _nodeId is { } id ? r.NodeId == id : r.NodeId is null && string.IsNullOrWhiteSpace(r.Section);

    private static string[] BuildRoleOptions(IReadOnlyList<Role> roles, RoleSlot slot)
    {
        var options = roles.Where(r => slot == RoleSlot.None || r.Slot == slot).Select(r => r.Name).ToList();
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
