using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class OperatorPromptViewModel : ObservableObject
{
    public OperatorPromptViewModel(
        IReadOnlyList<string>? callSignOptions = null,
        bool collectHost = false,
        IReadOnlyList<Person>? personnel = null,
        SessionOperator? previous = null)
    {
        CollectsHost = collectHost;
        _callSignOptions = callSignOptions ?? Array.Empty<string>();
        _personnel = personnel ?? Array.Empty<Person>();
        _personOptions = OwnDisplayNames(_personnel);
        PreviousOperatorDisplay = previous?.Display;
    }

    private IReadOnlyList<Person> _personnel;

    // Own personnel offered as suggestions for the NAME field (#469, #459); a neighbouring brigade's
    // people are left out (#458). Free text stays allowed: whoever documents need not be in the
    // roster, which is empty until imported. The call-sign prefill still searches the whole roster,
    // so a foreign name typed by hand gets its Funkrufname too. In the join flow these come from the
    // host once it is reached (ShowOperatorStage), since the host is the Stammdaten master (#183).
    [ObservableProperty]
    private IReadOnlyList<string> _personOptions;

    private static string[] OwnDisplayNames(IEnumerable<Person> personnel) =>
        personnel.Where(p => p.IsOwn).Select(p => p.DisplayName).ToArray();

    // Set only when a Lagebuchführer hands over mid-incident (#469): the prompt then asks for the
    // successor, and says who is being replaced so nobody confirms the wrong handover.
    public string? PreviousOperatorDisplay { get; }

    public bool IsHandover => PreviousOperatorDisplay is not null;

    public string Title =>
        IsHandover ? "Lagebuchführer wechseln"
        : IsHostStage ? "Mit Gerät verbinden"
        : "Wer dokumentiert?";

    // True only for the join flow (§6). That prompt runs in two stages (#459): host and PIN first,
    // then -- once the host is reached -- who documents here, suggested from the host's Stammdaten.
    public bool CollectsHost { get; }

    // The join flow's second stage: the host is reached, the operator is being asked for.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHostStage))]
    [NotifyPropertyChangedFor(nameof(AsksForOperator))]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isOperatorStage;

    /// <summary>Whether the prompt shows host and PIN (the join flow's first stage).</summary>
    public bool IsHostStage => CollectsHost && !IsOperatorStage;

    /// <summary>Whether the prompt shows name and Funkrufname: always, except while reaching a host.</summary>
    public bool AsksForOperator => !IsHostStage;

    public string ConfirmLabel => IsHostStage ? "VERBINDEN" : "BESTÄTIGEN";

    /// <summary>The incident being joined, "‹Stichwort› · ‹Adresse›", once the host is reached.</summary>
    [ObservableProperty]
    private string? _joinedIncidentDisplay;

    // Quiet until the first press: a prompt that opens already scolding teaches nothing.
    private bool _errorsShown;

    // The host's LAN or Tailscale address/name, entered only in the join flow. Mandatory there (gates Confirm).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HostError))]
    private string _host = string.Empty;

    // The share PIN the host displays, entered only in the join flow. Mandatory there (gates Confirm).
    // Read separately by the caller after Confirm — it is not part of SessionOperator.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinError))]
    private string _pin = string.Empty;

    // Radio call signs offered as dropdown suggestions for the Funkrufname field. The field stays
    // free-text (an operator's call sign need not be in the master list), so this is only a hint;
    // empty when a caller supplies none, in which case the control is a plain text box.
    [ObservableProperty]
    private IReadOnlyList<string> _callSignOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperatorNameError))]
    private string _operatorName = string.Empty;

    // Picking a person from the roster fills in their Funkrufname, but only into a blank field: a
    // call sign typed by hand outranks the roster, as in RolesViewModel.PrefillFromRoster.
    partial void OnOperatorNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(OperatorCallSign))
        {
            return;
        }

        var person = _personnel.FirstOrDefault(
            p => string.Equals(p.DisplayName, value, StringComparison.OrdinalIgnoreCase));
        if (person is not null)
        {
            OperatorCallSign = person.CallSign;
        }
    }

    [ObservableProperty]
    private string? _operatorCallSign;

    // Set while a join attempt is in flight (#182), so Confirm can't be double-clicked mid-request.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(CancelLabel))]
    private bool _isBusy;

    // #196: the Cancel button's own label needs to say which of its two very different effects it
    // currently has -- "ABBRECHEN" alone reads as "close this dialog", which is wrong while busy,
    // where it only aborts the connection attempt and leaves the dialog (and every typed field) up.
    public string CancelLabel => IsBusy ? "VERBINDUNG ABBRECHEN" : "ABBRECHEN";

    // The failed join's message, shown inline so the dialog can stay open for a retry instead of
    // closing and losing every field the operator already typed (#182).
    [ObservableProperty]
    private string? _errorMessage;

    // True only when the last join failure was a TOFU certificate-changed rejection; drives the
    // in-dialog "reset trust" button (mirrors HomeViewModel.CanResetTrustedCertificate from #181).
    [ObservableProperty]
    private bool _certificateChanged;

    private SessionOperator? _result;

    public SessionOperator? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    /// <summary>Whether the documenting operator's name is missing, once asked (#412).</summary>
    public string? OperatorNameError =>
        _errorsShown && AsksForOperator && string.IsNullOrWhiteSpace(OperatorName) ? ValidationMessages.Required : null;

    /// <summary>Whether the host address is missing in the join flow, once asked (#412).</summary>
    public string? HostError =>
        _errorsShown && IsHostStage && string.IsNullOrWhiteSpace(Host) ? ValidationMessages.Required : null;

    /// <summary>Whether the share PIN is missing in the join flow, once asked (#412).</summary>
    public string? PinError =>
        _errorsShown && IsHostStage && string.IsNullOrWhiteSpace(Pin) ? ValidationMessages.Required : null;

    // IsBusy stays: a join attempt is in flight, so there is genuinely nothing to press again and
    // nothing a field could explain. The empty fields answer on the press instead (#412).
    private bool CanConfirm => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _errorsShown = true;
        OnPropertyChanged(nameof(OperatorNameError));
        OnPropertyChanged(nameof(HostError));
        OnPropertyChanged(nameof(PinError));
        if (OperatorNameError is not null || HostError is not null || PinError is not null)
        {
            return;
        }

        if (IsHostStage)
        {
            ConnectRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Result = new SessionOperator(OperatorName, OperatorCallSign);
    }

    // Raised by Confirm in the join flow's first stage: host and PIN are filled in, reach the host.
    // The host answers with ShowOperatorStage or ReportJoinFailure.
    public event EventHandler? ConnectRequested;

    /// <summary>
    /// Moves the join flow on to asking who documents here (#459), now that the host is reached:
    /// names the incident being joined, and swaps the suggestions for the host's own personnel and
    /// call signs. What was already typed into NAME and FUNKRUFNAME stays.
    /// </summary>
    public void ShowOperatorStage(Incident joined, MasterDataSet hostMasterData)
    {
        ArgumentNullException.ThrowIfNull(joined);
        ArgumentNullException.ThrowIfNull(hostMasterData);

        _personnel = hostMasterData.Personnel;
        PersonOptions = OwnDisplayNames(_personnel);
        CallSignOptions = hostMasterData.RadioCallSigns;
        var keyword = string.IsNullOrWhiteSpace(joined.Keyword) ? "Unbenannter Einsatz" : joined.Keyword;
        JoinedIncidentDisplay = Formatting.Address(joined.Street, joined.District) is { } address
            ? $"{keyword} · {address}"
            : keyword;
        ErrorMessage = null;
        CertificateChanged = false;

        // A fresh stage starts quiet, like a fresh prompt: the name was never asked for yet.
        _errorsShown = false;
        IsOperatorStage = true;
        OnPropertyChanged(nameof(OperatorNameError));
    }

    // Called by the host after a failed join attempt (#182): reports the error inline, clears only
    // the PIN (Host/Name/Funkrufname stay as typed), and resets Result so the next Confirm() click
    // produces a fresh non-null value and re-triggers the host's existing Result-changed handler.
    // A failure after the host was reached spent that connection, so the prompt goes back to asking
    // for host and PIN, where the error is shown (#459).
    public void ReportJoinFailure(string message, bool certificateChanged)
    {
        ErrorMessage = message;
        CertificateChanged = certificateChanged;
        Pin = string.Empty;
        Result = null;
        IsOperatorStage = false;
        JoinedIncidentDisplay = null;
    }

    // Raised when the operator dismisses an idle prompt (e.g. Escape). Hosts clear the overlay.
    public event EventHandler? Cancelled;

    // Raised instead of Cancelled while a join is in flight (#196): there is a connection attempt
    // to abort, not the dialog to dismiss. Once the HomeViewModel join command unwinds from the
    // cancellation, IsBusy flips back to false but the prompt itself stays up -- every typed field
    // survives so the operator can retry immediately (see MainWindowViewModel.CancelJoin).
    public event EventHandler? CancelJoinRequested;

    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            CancelJoinRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    // Raised when the operator asks to reset TOFU trust for the host from within the dialog
    // (#182), after a CertificateChanged join failure. Hosts relay this to
    // HomeViewModel.ResetTrustedCertificateCommand, which owns the trust store.
    public event EventHandler? ResetTrustRequested;

    [RelayCommand]
    private void ResetTrust() => ResetTrustRequested?.Invoke(this, EventArgs.Empty);
}
