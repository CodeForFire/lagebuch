using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class OperatorPromptViewModel : ObservableObject
{
    public OperatorPromptViewModel(
        bool collectKeyword = false,
        IReadOnlyList<string>? callSignOptions = null,
        bool collectHost = false)
    {
        CollectsKeyword = collectKeyword;
        CollectsHost = collectHost;
        CallSignOptions = callSignOptions ?? Array.Empty<string>();
    }

    // True only for the new-incident flow; the continue-editing flow leaves it false.
    public bool CollectsKeyword { get; }

    // True only for the join flow (§6): show the host address field on top of the operator prompt,
    // so the joining device says who documents here and which host to reach in one step.
    public bool CollectsHost { get; }

    // The host's LAN or Tailscale address/name, entered only in the join flow. Mandatory there (gates Confirm).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _host = string.Empty;

    // The share PIN the host displays, entered only in the join flow. Mandatory there (gates Confirm).
    // Read separately by the caller after Confirm — it is not part of SessionOperator.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _pin = string.Empty;

    // Radio call signs offered as dropdown suggestions for the Funkrufname field. The field stays
    // free-text (an operator's call sign need not be in the master list), so this is only a hint;
    // empty when a caller supplies none, in which case the control is a plain text box.
    public IReadOnlyList<string> CallSignOptions { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string? _operatorCallSign;

    // The Stichwort (e.g. "B3P") — collected instead of the Einsatznummer, which is unknown at the
    // start of most incidents and not worth blocking on (#69). Free text, optional: an incident may
    // never get one. The Einsatznummer itself can be added later, from the workspace header.
    [ObservableProperty]
    private string? _keyword;

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

    private bool CanConfirm =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(OperatorName) &&
        (!CollectsHost || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Pin)));

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new SessionOperator(OperatorName, OperatorCallSign);
    }

    // Called by the host after a failed join attempt (#182): reports the error inline, clears only
    // the PIN (Host/Name/Funkrufname stay as typed), and resets Result so the next Confirm() click
    // produces a fresh non-null value and re-triggers the host's existing Result-changed handler.
    public void ReportJoinFailure(string message, bool certificateChanged)
    {
        ErrorMessage = message;
        CertificateChanged = certificateChanged;
        Pin = string.Empty;
        Result = null;
    }

    // Raised when the operator dismisses an idle prompt (e.g. Escape). Hosts clear the overlay.
    public event EventHandler? Cancelled;

    // Raised instead of Cancelled while a join is in flight (#196): there is a connection attempt
    // to abort, not the dialog to dismiss. Once HomeViewModel.JoinDeviceAsync unwinds from the
    // cancellation, IsBusy flips back to false but the prompt itself stays up -- every typed field
    // survives so the operator can retry immediately (see MainWindowViewModel.ConfirmOperatorAsync).
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
