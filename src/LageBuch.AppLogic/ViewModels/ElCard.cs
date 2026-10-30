using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Einsatzleitung Feuerwehr at the top of the Führung board: how it is reached, who leads it,
/// its Melder and Führungsassistenten, and the Führungsstufe set against the one the shape of the Einsatz
/// suggests. One instance for the board's lifetime, updated in place.
/// </summary>
public sealed partial class ElCard : ObservableObject
{
    internal const string NoStufeText = "Keine Führungsstufe festgelegt";

    private readonly Action<Fuehrungsstufe?> _onSelectStufe;
    private readonly Action<Guid, RoleAssignmentDialogMode>? _onAssistant;

    /// <param name="onSelectStufe">Sets the Führungsstufe (null clears it); the board decides whether that is allowed.</param>
    /// <param name="onEditPost">Opens the dialog for the Einsatzleitung's own post.</param>
    /// <param name="canEdit">False on a read-only Einsatz.</param>
    /// <param name="onPerson">Opens the person dialog for the Einsatzleitung and the given post.</param>
    /// <param name="onAssistant">Opens the dialog that corrects or hands over a Führungsunterstützung.</param>
    public ElCard(
        Action<Fuehrungsstufe?> onSelectStufe,
        Action? onEditPost = null,
        bool canEdit = true,
        Action<RoleSlot>? onPerson = null,
        Action<Guid, RoleAssignmentDialogMode>? onAssistant = null)
    {
        ArgumentNullException.ThrowIfNull(onSelectStufe);
        _onSelectStufe = onSelectStufe;
        _onAssistant = canEdit ? onAssistant : null;
        EditPostCommand = new RelayCommand(() => onEditPost?.Invoke(), () => canEdit);
        SetLeiterCommand = new RelayCommand(() => onPerson?.Invoke(RoleSlot.Leiter), () => canEdit);
        SetMelderCommand = new RelayCommand(() => onPerson?.Invoke(RoleSlot.Melder), () => canEdit);
        AddAssistantCommand = new RelayCommand(() => onPerson?.Invoke(RoleSlot.Fuehrungsassistent), () => canEdit);
        StufeChoices = BuildStufeChoices(null);
    }

    /// <summary>Opens the dialog for the Standort, Rufname, Telefon and Funkgruppen; cannot execute on a read-only Einsatz.</summary>
    public IRelayCommand EditPostCommand { get; }

    /// <summary>Sets the Einsatzleiter, handing over from a running one; off on a read-only Einsatz.</summary>
    public IRelayCommand SetLeiterCommand { get; }

    /// <summary>Sets the Einsatzleitung's Melder, handing over from a running one; off on a read-only Einsatz.</summary>
    public IRelayCommand SetMelderCommand { get; }

    /// <summary>Adds a Führungsunterstützung (S1 to S6); off on a read-only Einsatz.</summary>
    public IRelayCommand AddAssistantCommand { get; }

    [ObservableProperty]
    private string? _standort;

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

    [ObservableProperty]
    private string _stufeText = NoStufeText;

    [ObservableProperty]
    private string _suggestionText = string.Empty;

    [ObservableProperty]
    private bool _matchesSuggestion;

    public ObservableCollection<BoardPersonRow> Assistants { get; } = [];

    /// <summary>
    /// Further Leiter and Melder at the Einsatzleitung beyond the posts (an EAL typed on FUNKTIONEN
    /// without an Abschnitt): shown under "Weitere", so nobody the Bericht prints is missing here.
    /// </summary>
    public ObservableCollection<BoardPersonRow> Further { get; } = [];

    /// <summary>Stufe A to D, then "Keine"; replaced only on a real change (#408).</summary>
    public IReadOnlyList<StufeChoice> StufeChoices { get; private set; }

    /// <summary>Funkgruppen of one kind as the card shows them, one per line; null when there are none.</summary>
    internal static string? Lines(IReadOnlyList<string> groups) => groups.Count == 0 ? null : string.Join('\n', groups);

    /// <summary>Takes the Einsatzleitung row of the reporting line and the incident's post and Stufe.</summary>
    public void Update(ReportingEntry entry, LeadershipPost post, Fuehrungsstufe? stufe, FuehrungsstufeSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(suggestion);

        Standort = post.Standort;
        Rufname = post.Rufname;
        Telefon = post.Telefon;
        Tmo = Lines(post.Funkgruppen.Tmo);
        Dmo = Lines(post.Funkgruppen.Dmo);
        Leiter = BoardPersonRow.Follow(Leiter, entry.Leiter);
        Melder = BoardPersonRow.Follow(Melder, entry.Melder);
        BoardPersonRow.Reconcile(Assistants, entry.Others, _onAssistant);
        BoardPersonRow.Reconcile(Further, entry.Further);
        HasFurther = Further.Count > 0;
        StufeText = stufe is { } set ? $"Stufe {FuehrungsstufeLabels.Of(set)}" : NoStufeText;
        SuggestionText = $"Vorschlag: Stufe {FuehrungsstufeLabels.Of(suggestion.Stufe)} – {suggestion.Reason}";
        MatchesSuggestion = stufe == suggestion.Stufe;

        var choices = BuildStufeChoices(stufe);
        if (!choices.Select(Key).SequenceEqual(StufeChoices.Select(Key)))
        {
            StufeChoices = choices;
            OnPropertyChanged(nameof(StufeChoices));
        }
    }

    private static (Fuehrungsstufe? Stufe, string Label, bool IsCurrent) Key(StufeChoice c) => (c.Stufe, c.Label, c.IsCurrent);

    private StufeChoice[] BuildStufeChoices(Fuehrungsstufe? current)
    {
        Fuehrungsstufe?[] stufen = [Fuehrungsstufe.A, Fuehrungsstufe.B, Fuehrungsstufe.C, Fuehrungsstufe.D, null];
        return [.. stufen.Select(s => new StufeChoice(
            s,
            s is { } set ? $"Stufe {FuehrungsstufeLabels.Of(set)}" : "Keine",
            s == current,
            new RelayCommand(() => _onSelectStufe(s))))];
    }
}

/// <summary>
/// One entry of the Führungsstufe picker: a Stufe, or <c>null</c> for "Keine". A command per entry for
/// the reason documented on <see cref="SafetyTruppChoice"/>.
/// </summary>
/// <param name="Stufe">The Führungsstufe, or null for none.</param>
/// <param name="Label">What the entry says.</param>
/// <param name="IsCurrent">Whether this is the Stufe in force, for the check mark.</param>
/// <param name="SelectCommand">Sets the Stufe.</param>
public sealed record StufeChoice(Fuehrungsstufe? Stufe, string Label, bool IsCurrent, IRelayCommand SelectCommand);
