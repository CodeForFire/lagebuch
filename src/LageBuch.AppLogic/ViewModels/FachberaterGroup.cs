using System.Collections.ObjectModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// All Fachberater as one card on the Führung board's top row, after the partner Einsatzleitungen.
/// One instance for the board's lifetime; <see cref="People"/> is the board's own Fachberater list.
/// </summary>
public sealed class FachberaterGroup
{
    internal FachberaterGroup(ObservableCollection<BoardPersonRow> people)
    {
        ArgumentNullException.ThrowIfNull(people);
        People = people;
    }

    public ObservableCollection<BoardPersonRow> People { get; }
}
