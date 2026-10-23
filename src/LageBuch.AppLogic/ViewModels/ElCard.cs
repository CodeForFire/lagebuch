using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Einsatzleitung Feuerwehr at the top of the Führung board: how it is reached, who leads it,
/// its Führungsassistenten, and the Führungsstufe set against the one the shape of the Einsatz
/// suggests. One instance for the board's lifetime, updated in place.
/// </summary>
public sealed partial class ElCard : ObservableObject
{
    internal const string NoStufeText = "Keine Führungsstufe festgelegt";

    private readonly Action<Fuehrungsstufe?> _onSelectStufe;

    /// <param name="onSelectStufe">Sets the Führungsstufe (null clears it); the board decides whether that is allowed.</param>
    /// <param name="onEditPost">Opens the dialog for the Einsatzleitung's own post.</param>
    /// <param name="canEdit">False on a read-only Einsatz.</param>
    public ElCard(Action<Fuehrungsstufe?> onSelectStufe, Action? onEditPost = null, bool canEdit = true)
    {
        ArgumentNullException.ThrowIfNull(onSelectStufe);
        _onSelectStufe = onSelectStufe;
        EditPostCommand = new RelayCommand(() => onEditPost?.Invoke(), () => canEdit);
        StufeChoices = BuildStufeChoices(null);
    }

    /// <summary>Opens the dialog for the Standort, Rufname, Telefon and Funkgruppe; cannot execute on a read-only Einsatz.</summary>
    public IRelayCommand EditPostCommand { get; }

    [ObservableProperty]
    private string? _standort;

    [ObservableProperty]
    private string? _rufname;

    [ObservableProperty]
    private string? _telefon;

    [ObservableProperty]
    private string? _funkgruppe;

    /// <summary>The Einsatzleiter: a running Leiter with no node and no free-text Abschnitt.</summary>
    [ObservableProperty]
    private BoardPersonRow? _leiter;

    [ObservableProperty]
    private string _stufeText = NoStufeText;

    [ObservableProperty]
    private string _suggestionText = string.Empty;

    [ObservableProperty]
    private bool _matchesSuggestion;

    public ObservableCollection<BoardPersonRow> Assistants { get; } = [];

    /// <summary>Stufe A to D, then "Keine"; replaced only on a real change (#408).</summary>
    public IReadOnlyList<StufeChoice> StufeChoices { get; private set; }

    /// <summary>Takes the Einsatzleitung row of the reporting line and the incident's post and Stufe.</summary>
    public void Update(ReportingEntry entry, LeadershipPost post, Fuehrungsstufe? stufe, FuehrungsstufeSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(suggestion);

        Standort = post.Standort;
        Rufname = post.Rufname;
        Telefon = post.Telefon;
        Funkgruppe = post.Funkgruppe;
        Leiter = BoardPersonRow.Follow(Leiter, entry.Leiter);
        BoardPersonRow.Reconcile(Assistants, entry.Others);
        StufeText = stufe is { } set ? $"Stufe {LeadershipKeys.StufeKey(set)}" : NoStufeText;
        SuggestionText = $"Vorschlag: Stufe {LeadershipKeys.StufeKey(suggestion.Stufe)} – {suggestion.Reason}";
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
            s is { } set ? $"Stufe {LeadershipKeys.StufeKey(set)}" : "Keine",
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
