using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Equipment;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One line of the Ausgabeliste. The text stays editable for the whole Einsatz — a phone number is
/// often only known later — and writes straight through to the domain, following
/// <see cref="InvolvedPartyRow"/>: the view pushes on LostFocus, so a correction is one write, not
/// one per keystroke. ZURÜCK is the one-click return. A closed incident is a historical record: the
/// setters and commands are inert instead of throwing.
/// </summary>
public sealed partial class IssuedItemRow : ObservableObject
{
    private readonly Action<string, string, string?, string?, string?> _onEdited;
    private readonly Action _onReturned;
    private readonly Action _onReturnUndone;
    private readonly Action _onRemoved;

    public IssuedItemRow(
        IssuedItem item,
        int number,
        bool isReadOnly,
        Action<string, string, string?, string?, string?> onEdited,
        Action onReturned,
        Action onReturnUndone,
        Action onRemoved)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(onEdited);
        ArgumentNullException.ThrowIfNull(onReturned);
        ArgumentNullException.ThrowIfNull(onReturnUndone);
        ArgumentNullException.ThrowIfNull(onRemoved);
        Id = item.Id;
        Number = number.ToString(CultureInfo.InvariantCulture);
        IsOut = item.IsOut;
        IsReadOnly = isReadOnly;
        IssuedDisplay = $"Ausgabe {Formatting.TimeOfDay(item.IssuedAt)}";
        ReturnedDisplay = item.ReturnedAt is { } at ? $"Zurück {Formatting.TimeOfDay(at)}" : null;
        _onEdited = onEdited;
        _onReturned = onReturned;
        _onReturnUndone = onReturnUndone;
        _onRemoved = onRemoved;
        _device = item.Device;
        _issuedTo = item.IssuedTo;
        _issuedToPhone = item.IssuedToPhone;
        _issuedToDetails = item.IssuedToDetails;
        _remark = item.Remark;
    }

    public Guid Id { get; }

    /// <summary>The line's number in issue order, as the paper Ausgabeliste counts it: "1", "2".</summary>
    public string Number { get; }

    /// <summary>Whether the Gerät is still out; the row is marked and offers ZURÜCK while it is.</summary>
    public bool IsOut { get; }

    public bool IsReadOnly { get; }

    /// <summary>"Ausgabe 14:02".</summary>
    public string IssuedDisplay { get; }

    /// <summary>"Zurück 14:45", or null while the Gerät is still out.</summary>
    public string? ReturnedDisplay { get; }

    [ObservableProperty]
    private string _device;

    [ObservableProperty]
    private string _issuedTo;

    [ObservableProperty]
    private string? _issuedToPhone;

    [ObservableProperty]
    private string? _issuedToDetails;

    [ObservableProperty]
    private string? _remark;

    /// <summary>Why the last correction was not taken, or null. Cleared by the rebuild that
    /// follows the next accepted write.</summary>
    [ObservableProperty]
    private string? _error;

    partial void OnDeviceChanged(string value) => Push();

    partial void OnIssuedToChanged(string value) => Push();

    partial void OnIssuedToPhoneChanged(string? value) => Push();

    partial void OnIssuedToDetailsChanged(string? value) => Push();

    partial void OnRemarkChanged(string? value) => Push();

    private void Push()
    {
        if (IsReadOnly)
        {
            return;
        }

        Error = IssuedItemsViewModel.Validate(Device, IssuedTo, IssuedToPhone, IssuedToDetails, Remark);
        if (Error is null)
        {
            _onEdited(Device, IssuedTo, IssuedToPhone, IssuedToDetails, Remark);
        }
    }

    // The body guards also cover a programmatic Execute, which bypasses CanExecute.
    [RelayCommand(CanExecute = nameof(CanReturn))]
    private void Return()
    {
        if (CanReturn)
        {
            _onReturned();
        }
    }

    private bool CanReturn => !IsReadOnly && IsOut;

    [RelayCommand(CanExecute = nameof(CanUndoReturn))]
    private void UndoReturn()
    {
        if (CanUndoReturn)
        {
            _onReturnUndone();
        }
    }

    private bool CanUndoReturn => !IsReadOnly && !IsOut;

    /// <summary>Takes the line back after a confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (CanRemove)
        {
            _onRemoved();
        }
    }

    private bool CanRemove => !IsReadOnly;
}

/// <summary>
/// The AUSGABE tab, the Ausgabeliste: the Geräte the ELW hands out — Mobilteile, Westen, the AED —
/// to whom, and whether they came back. Deliberately writes no ETB line: the recipient's name and
/// phone number stay in this one list. What is still out is listed first, so the end of an Einsatz
/// starts with what to collect.
/// </summary>
public sealed partial class IssuedItemsViewModel : ObservableObject, INarrowAware, IEntryForm, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly Action<string, string, Action> _requestConfirm;
    private readonly IReadOnlyList<Device> _devices;
    private readonly List<string> _deviceNames;
    private readonly IReadOnlyList<ContactEntry> _contacts;

    // Quiet until the first press: a tab that opens already scolding teaches nothing.
    private bool _errorsShown;

    // Each suggested name's phone number, keyed on the trimmed name, ignoring case.
    private Dictionary<string, string?> _issuedToPhones = new(StringComparer.OrdinalIgnoreCase);

    // The number a pick put into TELEFON, so that a different name takes it out again; null once
    // the Lagebuchführer typed a number of their own.
    private string? _autoFilledPhone;

    /// <param name="session">The incident session every change goes through.</param>
    /// <param name="onChanged">Called after each accepted change, as every module does.</param>
    /// <param name="devices">
    /// The Stammdaten's Geräte, offered as suggestions, never a closed set. Each one's Anzahl is
    /// checked against what is still out: a Gerät with none left is no longer suggested, and one
    /// typed anyway is asked about before it is handed out.
    /// </param>
    /// <param name="requestConfirm">
    /// Asks the host to confirm a removal, or an Ausgabe of a Gerät with none left, before running it,
    /// as <see cref="InvolvedPartiesViewModel"/> does. Defaults to running it immediately, so tests
    /// that do not care need not supply one.
    /// </param>
    /// <param name="contacts">
    /// Everybody the Kontakte tab lists, offered after the Beteiligte as AUSGEGEBEN AN suggestions
    /// (#837), never a closed set.
    /// </param>
    public IssuedItemsViewModel(
        IIncidentSession session,
        Action onChanged,
        IReadOnlyList<Device>? devices = null,
        Action<string, string, Action>? requestConfirm = null,
        IReadOnlyList<ContactEntry>? contacts = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        _requestConfirm = requestConfirm ?? ((_, _, onConfirmed) => onConfirmed());
        _devices = devices ?? [];
        _deviceNames = _devices.Select(d => d.Name).ToList();
        _contacts = contacts ?? [];
        IsReadOnly = session.IsReadOnly;
        IssuedToOptions = BuildIssuedToOptions();
        Items = new ObservableCollection<IssuedItemRow>(BuildRows());
        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    /// <summary>
    /// The Stammdaten's Geräte for the GERÄT suggestions, leaving out every one whose counted Anzahl
    /// is all ausgegeben until one comes back (#838). A Gerät the Stammdaten do not count is always
    /// offered.
    /// </summary>
    public IReadOnlyList<string> DeviceOptions =>
        _deviceNames.Where(name => StockOf(name) is not var (issued, count) || issued < count).ToList();

    /// <summary>
    /// The AUSGEGEBEN AN suggestions: this Einsatz's Beteiligte first, then the Kontakte, each name
    /// once. Rebuilt on every change, so a Beteiligter recorded meanwhile — here or by a joined
    /// device — is offered at once.
    /// </summary>
    public IReadOnlyList<string> IssuedToOptions { get; private set; }

    /// <summary>What is still out first, then what came back; each group in issue order.</summary>
    public ObservableCollection<IssuedItemRow> Items { get; }

    /// <summary>"2 Geräte ausgegeben", "Alle Geräte zurück", or null while the list is empty.</summary>
    public string? Summary => _session.Incident switch
    {
        { IssuedItems.Count: 0 } => null,
        { ItemsOut: 0 } => "Alle Geräte zurück",
        { ItemsOut: 1 } => "1 Gerät ausgegeben",
        var incident => $"{incident.ItemsOut} Geräte ausgegeben",
    };

    /// <summary>Whether a Gerät is still out, which colours the summary as a warning.</summary>
    public bool HasItemsOut => _session.Incident.ItemsOut > 0;

    /// <summary>The caps the view hands to each box's MaxLength, so typing stops where the domain
    /// would refuse.</summary>
    public static int MaxDeviceLength => IssuedItem.MaxDeviceLength;

    public static int MaxIssuedToLength => IssuedItem.MaxIssuedToLength;

    public static int MaxPhoneLength => IssuedItem.MaxPhoneLength;

    public static int MaxDetailsLength => IssuedItem.MaxDetailsLength;

    public static int MaxRemarkLength => IssuedItem.MaxRemarkLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewItemError))]
    [NotifyPropertyChangedFor(nameof(IsNewDeviceMissing))]
    [NotifyPropertyChangedFor(nameof(NewDeviceStock))]
    [NotifyPropertyChangedFor(nameof(IsNewDeviceExhausted))]
    private string? _newDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewItemError))]
    [NotifyPropertyChangedFor(nameof(IsNewIssuedToMissing))]
    private string? _newIssuedTo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewItemError))]
    private string? _newIssuedToPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewItemError))]
    private string? _newIssuedToDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewItemError))]
    private string? _newRemark;

    // A known name brings its number into an empty TELEFON (#837); a number typed by hand is never
    // overwritten. Moving on to another name takes back only a number the pick put there.
    partial void OnNewIssuedToChanged(string? value)
    {
        if (_autoFilledPhone is not null && string.Equals(NewIssuedToPhone, _autoFilledPhone, StringComparison.Ordinal))
        {
            NewIssuedToPhone = null;
        }

        _autoFilledPhone = null;
        if (string.IsNullOrWhiteSpace(NewIssuedToPhone)
            && value is not null
            && _issuedToPhones.TryGetValue(value.Trim(), out var phone)
            && !string.IsNullOrWhiteSpace(phone))
        {
            NewIssuedToPhone = phone;
            _autoFilledPhone = phone;
        }
    }

    /// <summary>
    /// How many of the typed Gerät are still in the ELW: "2 von 3 frei", or "Kein Gerät frei: 3 von 3
    /// ausgegeben". Null for a Gerät the Stammdaten do not count.
    /// </summary>
    public string? NewDeviceStock => StockOf(NewDevice) switch
    {
        null => null,
        var (issued, count) when issued < count => $"{count - issued} von {count} frei",
        var (issued, count) => $"Kein Gerät frei: {issued} von {count} ausgegeben",
    };

    /// <summary>Whether every counted one of the typed Gerät is out, which colours the hint as a
    /// warning and has AUSGEBEN ask first.</summary>
    public bool IsNewDeviceExhausted => StockOf(NewDevice) is var (issued, count) && issued >= count;

    /// <summary>What stops the Ausgabe from being recorded, once the Lagebuchführer has asked.</summary>
    public string? NewItemError =>
        _errorsShown ? Validate(NewDevice, NewIssuedTo, NewIssuedToPhone, NewIssuedToDetails, NewRemark) : null;

    /// <summary>Marks GERÄT invalid once asked, so a refused press puts focus there.</summary>
    public bool IsNewDeviceMissing => _errorsShown && string.IsNullOrWhiteSpace(NewDevice);

    /// <summary>Marks AUSGEGEBEN AN invalid once asked, so a refused press puts focus there.</summary>
    public bool IsNewIssuedToMissing => _errorsShown && string.IsNullOrWhiteSpace(NewIssuedTo);

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
    public static string? Validate(string? device, string? issuedTo, string? phone, string? details, string? remark)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return ValidationMessages.DeviceRequired;
        }

        if (string.IsNullOrWhiteSpace(issuedTo))
        {
            return ValidationMessages.IssuedToRequired;
        }

        return TooLong(device, "Gerät", IssuedItem.MaxDeviceLength)
            ?? TooLong(issuedTo, "Ausgegeben an", IssuedItem.MaxIssuedToLength)
            ?? TooLong(phone, "Telefon", IssuedItem.MaxPhoneLength)
            ?? TooLong(details, "Organisation / Kontakt", IssuedItem.MaxDetailsLength)
            ?? TooLong(remark, "Bemerkung", IssuedItem.MaxRemarkLength);
    }

    private static string? TooLong(string? value, string field, int max) =>
        value is not null && value.Trim().Length > max ? $"{field} ist zu lang (höchstens {max} Zeichen)" : null;

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
        if (NewItemError is not null || NewDevice is null || NewIssuedTo is null)
        {
            EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Rejected);
            return;
        }

        // A typed "mobilteil 1 " records as the Stammdaten's "Mobilteil 1"; anything the list
        // lacks is kept as typed.
        // Against every Gerät, not just the ones still offered, so an exhausted one typed by hand
        // still counts toward its Anzahl.
        var device = StammdatenCatalogue.Normalize(NewDevice, _deviceNames) ?? NewDevice;
        var issuedTo = NewIssuedTo;
        if (StockOf(device) is var (issued, count) && issued >= count)
        {
            // Asked, never refused: a Gerät handed back without a ZURÜCK, or one borrowed from
            // another ELW, is still in someone's hand and still has to be recorded.
            _requestConfirm(
                "Kein Gerät mehr frei",
                $"Laut Stammdaten gibt es {count} × „{device}“, ausgegeben sind {issued}. Trotzdem ausgeben?",
                () => Issue(device, issuedTo));
            return;
        }

        Issue(device, issuedTo);
    }

    private void Issue(string device, string issuedTo)
    {
        _session.IssueItem(device, issuedTo, NewIssuedToPhone, NewIssuedToDetails, NewRemark); // Changed → Refresh
        NewDevice = null;
        NewIssuedTo = null;
        NewIssuedToPhone = null;
        NewIssuedToDetails = null;
        NewRemark = null;
        ShowErrors(false);
        IsComposerOpen = false;
        _onChanged();
        EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Succeeded);
    }

    // How many of the Gerät are out against how many the Stammdaten count, or null when they count
    // none. Matched ignoring case, like every catalogue lookup, so a line recorded before the
    // Stammdaten respelled the Gerät still counts.
    private (int Issued, int Count)? StockOf(string? device)
    {
        if (StammdatenCatalogue.Find(device, _devices) is not { Count: > 0 } entry)
        {
            return null;
        }

        var issued = _session.Incident.IssuedItems.Count(
            i => i.IsOut && string.Equals(i.Device, entry.Name, StringComparison.OrdinalIgnoreCase));
        return (issued, entry.Count);
    }

    private void ShowErrors(bool shown)
    {
        _errorsShown = shown;
        OnPropertyChanged(nameof(NewItemError));
        OnPropertyChanged(nameof(IsNewDeviceMissing));
        OnPropertyChanged(nameof(IsNewIssuedToMissing));
    }

    // Rebuild from the incident on any change — this device's edit, or (when joined) another's.
    private void Refresh()
    {
        Items.Clear();
        foreach (var row in BuildRows())
        {
            Items.Add(row);
        }

        // Replaced only when it really changed: a new list under an open suggestion box would
        // refilter what is being typed on every Ausgabe or ZURÜCK, here or on a joined device.
        var options = BuildIssuedToOptions();
        if (!options.SequenceEqual(IssuedToOptions, StringComparer.Ordinal))
        {
            IssuedToOptions = options;
            OnPropertyChanged(nameof(IssuedToOptions));
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasItemsOut));
        OnPropertyChanged(nameof(NewDeviceStock));
        OnPropertyChanged(nameof(IsNewDeviceExhausted));
        OnPropertyChanged(nameof(DeviceOptions));
    }

    // A name that is there twice is offered once, under its first spelling; its number is the
    // first one either list knows.
    private List<string> BuildIssuedToOptions()
    {
        var options = new List<string>();
        var phones = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var people = _session.Incident.InvolvedParties.Select(p => (p.Name, p.Phone))
            .Concat(_contacts.Select(c => (c.Name, c.Phone)))
            .Where(p => !string.IsNullOrWhiteSpace(p.Name));
        foreach (var (name, phone) in people)
        {
            var key = name.Trim();
            if (phones.TryGetValue(key, out var known))
            {
                phones[key] = string.IsNullOrWhiteSpace(known) ? phone : known;
                continue;
            }

            phones.Add(key, phone);
            options.Add(name);
        }

        _issuedToPhones = phones;
        return options;
    }

    private List<IssuedItemRow> BuildRows() =>
        _session.Incident.IssuedItems
            .Select((item, index) => ToRow(item, index + 1))
            .OrderBy(row => !row.IsOut)
            .ToList();

    private IssuedItemRow ToRow(IssuedItem item, int number) =>
        new(
            item,
            number,
            IsReadOnly,
            (device, issuedTo, phone, details, remark) =>
            {
                _session.UpdateIssuedItem(item.Id, device, issuedTo, phone, details, remark);
                _onChanged();
            },
            () =>
            {
                _session.ReturnIssuedItem(item.Id); // Changed → Refresh moves the row down
                _onChanged();
            },
            () =>
            {
                _session.UndoIssuedItemReturn(item.Id);
                _onChanged();
            },
            () => _requestConfirm(
                "Ausgabe entfernen?",
                $"„{item.Device}“ an „{item.IssuedTo}“ aus der Ausgabeliste entfernen. Fortfahren?",
                () =>
                {
                    _session.RemoveIssuedItem(item.Id); // Changed → Refresh drops the row
                    _onChanged();
                }));
}
