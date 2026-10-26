using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Funktionen typed on FUNKTIONEN with a free-text Abschnitt and no node: shown read-only on the
/// board, so nobody recorded there disappears from the sheet.
/// </summary>
public sealed partial class FreeTextSectionCard : ObservableObject
{
    public FreeTextSectionCard(string section, IReadOnlyList<RoleAssignment> people)
    {
        ArgumentNullException.ThrowIfNull(section);
        _section = section;
        Update(section, people);
    }

    /// <summary>The free-text Abschnitt as recorded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _section;

    /// <summary>"Abschnitt (Freitext): {Section}".</summary>
    public string Title => $"Abschnitt (Freitext): {Section}";

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
        BoardPersonRow.Reconcile(People, people);
    }
}
