using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Involved;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One Beteiligte/r in the list. Name, Telefon and Notiz stay editable for the whole Einsatz — a
/// phone number is often only known later — and write straight through to the domain, following
/// <see cref="ForceRow"/>. The view pushes on LostFocus, so a correction is one write, not one per
/// keystroke. A closed incident is a historical record: the setters are inert instead of throwing.
/// </summary>
public sealed partial class InvolvedPartyRow : ObservableObject
{
    private readonly Action<string, string?, string?> _onEdited;
    private readonly Action _onRemoved;

    public InvolvedPartyRow(
        InvolvedParty party,
        bool isReadOnly,
        Action<string, string?, string?> onEdited,
        Action onRemoved)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(onEdited);
        ArgumentNullException.ThrowIfNull(onRemoved);
        Id = party.Id;
        CreatedBy = party.CreatedBy;
        IsReadOnly = isReadOnly;
        _onEdited = onEdited;
        _onRemoved = onRemoved;
        _name = party.Name;
        _phone = party.Phone;
        _notes = party.Notes;
    }

    public Guid Id { get; }

    public string CreatedBy { get; }

    public bool IsReadOnly { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _phone;

    [ObservableProperty]
    private string? _notes;

    /// <summary>Why the last correction was not taken, or null. Cleared by the rebuild that
    /// follows the next accepted write.</summary>
    [ObservableProperty]
    private string? _error;

    partial void OnNameChanged(string value) => Push();

    partial void OnPhoneChanged(string? value) => Push();

    partial void OnNotesChanged(string? value) => Push();

    private void Push()
    {
        if (IsReadOnly)
        {
            return;
        }

        Error = InvolvedPartiesViewModel.Validate(Name, Phone, Notes);
        if (Error is null)
        {
            _onEdited(Name, Phone, Notes);
        }
    }

    /// <summary>Takes the entry back after a confirmation. Inert on a closed incident; the body
    /// guard also covers a programmatic Execute, which bypasses CanExecute.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (!CanRemove)
        {
            return;
        }

        _onRemoved();
    }

    private bool CanRemove => !IsReadOnly;
}

/// <summary>
/// The BETEILIGTE tab: the Einsatz's own address book of people who are not forces — the house
/// owner, the vehicle owner, the police contact. Deliberately writes no ETB line: a third party's
/// name and phone number stay in this one list.
/// </summary>
public sealed partial class InvolvedPartiesViewModel : ObservableObject, INarrowAware, IEntryForm, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly Action<string, Action> _requestConfirm;

    // Quiet until the first press: a tab that opens already scolding teaches nothing.
    private bool _errorsShown;

    /// <param name="session">The incident session every change goes through.</param>
    /// <param name="onChanged">Called after each accepted change, as every module does.</param>
    /// <param name="requestConfirm">
    /// Asks the host to confirm the removal before running it, as <see cref="ForcesViewModel"/>
    /// does. Defaults to running it immediately, so tests that do not care need not supply one.
    /// </param>
    public InvolvedPartiesViewModel(
        IIncidentSession session,
        Action onChanged,
        Action<string, Action>? requestConfirm = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        _requestConfirm = requestConfirm ?? ((_, onConfirmed) => onConfirmed());
        IsReadOnly = session.IsReadOnly;
        Parties = new ObservableCollection<InvolvedPartyRow>(session.Incident.InvolvedParties.Select(ToRow));
        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    public ObservableCollection<InvolvedPartyRow> Parties { get; }

    /// <summary>The caps the view hands to each TextBox's MaxLength, so typing stops where the
    /// domain would refuse.</summary>
    public static int MaxNameLength => InvolvedParty.MaxNameLength;

    public static int MaxPhoneLength => InvolvedParty.MaxPhoneLength;

    public static int MaxNotesLength => InvolvedParty.MaxNotesLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameError))]
    private string _newName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameError))]
    private string? _newPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameError))]
    private string? _newNotes;

    /// <summary>What stops the entry from being added, once the Lagebuchführer has asked.</summary>
    public string? NewNameError => _errorsShown ? Validate(NewName, NewPhone, NewNotes) : null;

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isNarrow;

    /// <summary>Whether the phone's add form is open; the wide layout always shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isComposerOpen;

    /// <summary>Whether the add form is on screen: always when wide, only while composing on a phone.</summary>
    public bool ShowComposer => !IsReadOnly && (!IsNarrow || IsComposerOpen);

    /// <summary>The phone's "add" affordance, shown exactly when the form is not.</summary>
    public bool ShowComposerButton => !IsReadOnly && IsNarrow && !IsComposerOpen;

    /// <summary>
    /// The first reason these values would be refused, in German, or null when they are fine.
    /// Mirrors the domain's rules so a bad value is explained instead of thrown.
    /// </summary>
    public static string? Validate(string? name, string? phone, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ValidationMessages.NameRequired;
        }

        if (name.Trim().Length > InvolvedParty.MaxNameLength)
        {
            return TooLong("Name", InvolvedParty.MaxNameLength);
        }

        if (phone is not null && phone.Trim().Length > InvolvedParty.MaxPhoneLength)
        {
            return TooLong("Telefon", InvolvedParty.MaxPhoneLength);
        }

        return notes is not null && notes.Trim().Length > InvolvedParty.MaxNotesLength
            ? TooLong("Notiz", InvolvedParty.MaxNotesLength)
            : null;
    }

    private static string TooLong(string field, int max) =>
        $"{field} ist zu lang (höchstens {max} Zeichen)";

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void OpenComposer() => IsComposerOpen = true;

    [RelayCommand]
    private void CloseComposer()
    {
        IsComposerOpen = false;
        ShowErrors(false); // a dismissed form must not reopen still complaining
    }

    // Only the read-only rule gates the button; the input rule answers on the press (#412).
    private bool CanAdd => !IsReadOnly;

    /// <inheritdoc />
    public event EventHandler<EntrySubmittedEventArgs>? EntrySubmitted;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        ShowErrors(true);
        if (NewNameError is not null)
        {
            EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Rejected);
            return;
        }

        _session.AddInvolvedParty(NewName, NewPhone, NewNotes); // Changed → Refresh
        NewName = string.Empty;
        NewPhone = null;
        NewNotes = null;
        ShowErrors(false);
        IsComposerOpen = false;
        _onChanged();
        EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Succeeded);
    }

    private void ShowErrors(bool shown)
    {
        _errorsShown = shown;
        OnPropertyChanged(nameof(NewNameError));
    }

    // Rebuild from the incident on any change — this device's edit, or (when joined) another's.
    private void Refresh()
    {
        Parties.Clear();
        foreach (var party in _session.Incident.InvolvedParties)
        {
            Parties.Add(ToRow(party));
        }
    }

    private InvolvedPartyRow ToRow(InvolvedParty party) =>
        new(
            party,
            IsReadOnly,
            (name, phone, notes) =>
            {
                _session.UpdateInvolvedParty(party.Id, name, phone, notes);
                _onChanged();
            },
            () => _requestConfirm(
                $"„{party.Name}“ aus den Beteiligten entfernen. Fortfahren?",
                () =>
                {
                    _session.RemoveInvolvedParty(party.Id); // Changed → Refresh drops the row
                    _onChanged();
                }));
}
