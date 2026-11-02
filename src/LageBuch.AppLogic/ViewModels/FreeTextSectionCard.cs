using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Funktionen typed on FUNKTIONEN with a free-text Abschnitt and no node: shown on the board, so
/// nobody recorded there disappears from the sheet, and taken into the Führung structure from here
/// in one step (#824). Each person can be corrected, handed over or ended like any on the board.
/// </summary>
public sealed partial class FreeTextSectionCard : ObservableObject
{
    private readonly Action<Guid, RoleAssignmentDialogMode>? _onOpen;

    /// <param name="section">The free-text Abschnitt as recorded.</param>
    /// <param name="people">The running Funktionen typed with it.</param>
    /// <param name="onAdopt">Takes the Abschnitt into the structure; null on a read-only Einsatz.</param>
    /// <param name="onOpen">Opens a person's correction or handover; null on a read-only Einsatz.</param>
    public FreeTextSectionCard(
        string section,
        IReadOnlyList<RoleAssignment> people,
        Action<string>? onAdopt = null,
        Action<Guid, RoleAssignmentDialogMode>? onOpen = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        _section = section;
        _onOpen = onOpen;
        AdoptCommand = new RelayCommand(() => onAdopt?.Invoke(Section), () => onAdopt is not null);
        Update(section, people);
    }

    /// <summary>The free-text Abschnitt as recorded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(AdoptAutomationName))]
    private string _section;

    /// <summary>"Abschnitt (Freitext): {Section}".</summary>
    public string Title => $"Abschnitt (Freitext): {Section}";

    /// <summary>ALS ABSCHNITT ÜBERNEHMEN: forms the Abschnitt, or uses the one of that name, and places everyone there.</summary>
    public IRelayCommand AdoptCommand { get; }

    /// <summary>The adopt button's automation name.</summary>
    public string AdoptAutomationName => $"Abschnitt {Section} übernehmen";

    public ObservableCollection<BoardPersonRow> People { get; } = [];

    /// <summary>
    /// The key the board reconciles these cards by: the grouping in
    /// <see cref="Domain.Leadership.ReportingLine.FreeTextSections"/> ignores case, so the key does too.
    /// </summary>
    internal static string KeyOf(string section) => section.ToUpperInvariant();

    public void Update(string section, IReadOnlyList<RoleAssignment> people)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(people);
        Section = section;
        BoardPersonRow.Reconcile(People, people, _onOpen);
    }
}
