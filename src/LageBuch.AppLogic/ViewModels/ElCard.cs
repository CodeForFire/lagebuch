using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    }
}
