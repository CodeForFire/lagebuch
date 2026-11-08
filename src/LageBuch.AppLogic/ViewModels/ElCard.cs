using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Einsatzleitung Feuerwehr at the top of the Führung board: how it is reached, who leads it,
/// and its Melder and Führungsassistenten. One instance for the board's lifetime, updated in place.
/// </summary>
public sealed partial class ElCard : ObservableObject
{
    private readonly Action<Guid, RoleAssignmentDialogMode>? _onAssistant;

    /// <param name="onEditPost">Opens the dialog for the Einsatzleitung's own post.</param>
    /// <param name="canEdit">False on a read-only Einsatz.</param>
    /// <param name="onPerson">Opens the person dialog for the Einsatzleitung and the given post.</param>
    /// <param name="onAssistant">Opens the dialog that corrects or hands over a Führungsunterstützung.</param>
    /// <param name="onTakeLagefuehrung">Hands the Lageführung back to the Einsatzleitung Feuerwehr (#926).</param>
    public ElCard(
        Action? onEditPost = null,
        bool canEdit = true,
        Action<RoleSlot>? onPerson = null,
        Action<Guid, RoleAssignmentDialogMode>? onAssistant = null,
        Action? onTakeLagefuehrung = null)
    {
        TakeLagefuehrungCommand = new RelayCommand(() => onTakeLagefuehrung?.Invoke(), () => canEdit);
        _onAssistant = canEdit ? onAssistant : null;
        EditPostCommand = new RelayCommand(() => onEditPost?.Invoke(), () => canEdit);
        SetLeiterCommand = new RelayCommand(() => onPerson?.Invoke(RoleSlot.Leiter), () => canEdit);
        SetMelderCommand = new RelayCommand(() => onPerson?.Invoke(RoleSlot.Melder), () => canEdit);
        AddAssistantCommand = new RelayCommand(() => onPerson?.Invoke(RoleSlot.Fuehrungsassistent), () => canEdit);
    }

    /// <summary>Opens the dialog for the Standort, Führungsfahrzeug, Rufname, Telefon and Funkgruppen; cannot execute on a read-only Einsatz.</summary>
    public IRelayCommand EditPostCommand { get; }

    /// <summary>Sets the Einsatzleiter, handing over from a running one; off on a read-only Einsatz.</summary>
    public IRelayCommand SetLeiterCommand { get; }

    /// <summary>Sets the Einsatzleitung's Melder, handing over from a running one; off on a read-only Einsatz.</summary>
    public IRelayCommand SetMelderCommand { get; }

    /// <summary>Adds a Führungsunterstützung (S1 to S6); off on a read-only Einsatz.</summary>
    public IRelayCommand AddAssistantCommand { get; }

    /// <summary>Hands the Lageführung back to the Einsatzleitung Feuerwehr; off on a read-only Einsatz.</summary>
    public IRelayCommand TakeLagefuehrungCommand { get; }

    /// <summary>The Lageführung state of this card; see <see cref="LagefuehrungMark"/>.</summary>
    [ObservableProperty]
    private LagefuehrungMark _lagefuehrung = LagefuehrungMark.None;

    [ObservableProperty]
    private string? _standort;

    /// <summary>The Funkrufname of the Einsatzleitung's Führungsfahrzeug (#829); null when none.</summary>
    [ObservableProperty]
    private string? _fuehrungsfahrzeug;

    [ObservableProperty]
    private string? _rufname;

    [ObservableProperty]
    private string? _telefon;

    [ObservableProperty]
    private string? _tmo;

    [ObservableProperty]
    private string? _dmo;

    /// <summary>The Einsatzleiter: a running Leiter with no node and no free-text Abschnitt.</summary>
    [ObservableProperty]
    private BoardPersonRow? _leiter;

    /// <summary>The Einsatzleitung's Melder: a running Melder with no node and no free-text Abschnitt.</summary>
    [ObservableProperty]
    private BoardPersonRow? _melder;

    /// <summary>Whether <see cref="Further"/> has anyone, for its "Weitere" heading.</summary>
    [ObservableProperty]
    private bool _hasFurther;

    public ObservableCollection<BoardPersonRow> Assistants { get; } = [];

    /// <summary>The TMO Funkgruppen one per line, unmarked: the Einsatzleitung is the level the others are checked against.</summary>
    public IReadOnlyList<FunkgruppeLine> TmoLines { get; private set; } = [];

    /// <summary>The DMO Funkgruppen one per line, as <see cref="TmoLines"/>.</summary>
    public IReadOnlyList<FunkgruppeLine> DmoLines { get; private set; } = [];

    /// <summary>
    /// Further Leiter and Melder at the Einsatzleitung beyond the posts (an EAL typed on FUNKTIONEN
    /// without an Abschnitt): shown under "Weitere", so nobody the Bericht prints is missing here.
    /// </summary>
    public ObservableCollection<BoardPersonRow> Further { get; } = [];

    /// <summary>Funkgruppen of one kind as the card shows them, one per line; null when there are none.</summary>
    internal static string? Lines(IReadOnlyList<string> groups) => groups.Count == 0 ? null : string.Join('\n', groups);

    /// <summary>Takes the Einsatzleitung row of the reporting line and the incident's post.</summary>
    public void Update(ReportingEntry entry, LeadershipPost post)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(post);

        Standort = post.Standort;
        Fuehrungsfahrzeug = post.Fuehrungsfahrzeug;
        Rufname = post.Rufname;
        Telefon = post.Telefon;
        Tmo = Lines(post.Funkgruppen.Tmo);
        Dmo = Lines(post.Funkgruppen.Dmo);
        if (FunkgruppeLine.Changed(TmoLines, FunkgruppeLine.Of(post.Funkgruppen.Tmo, null)) is { } tmo)
        {
            TmoLines = tmo;
            OnPropertyChanged(nameof(TmoLines));
        }

        if (FunkgruppeLine.Changed(DmoLines, FunkgruppeLine.Of(post.Funkgruppen.Dmo, null)) is { } dmo)
        {
            DmoLines = dmo;
            OnPropertyChanged(nameof(DmoLines));
        }

        Leiter = BoardPersonRow.Follow(Leiter, entry.Leiter);
        Melder = BoardPersonRow.Follow(Melder, entry.Melder);
        BoardPersonRow.Reconcile(Assistants, entry.Others, _onAssistant);
        BoardPersonRow.Reconcile(Further, entry.Further);
        HasFurther = Further.Count > 0;
    }
}
