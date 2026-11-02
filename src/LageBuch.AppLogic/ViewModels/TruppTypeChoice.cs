using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One entry in a row's Trupp-Art flyout (#835): a Trupp-Art from today's Stammdaten, or the one
/// the Trupp holds when the Stammdaten no longer list it.
/// </summary>
/// <remarks>
/// A command per entry rather than a ComboBox selection, for the reason <see cref="SafetyTruppChoice"/>
/// gives: the write comes straight back through the incident's Changed event and rebuilds the row.
/// </remarks>
public sealed class TruppTypeChoice
{
    public TruppTypeChoice(string name, bool isCurrent, Action<string> onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        Name = name;
        IsCurrent = isCurrent;
        SelectCommand = new RelayCommand(() => onSelect(name));
    }

    /// <summary>The Trupp-Art, as the Stammdaten spell it.</summary>
    public string Name { get; }

    /// <summary>Whether this is the Trupp's current Trupp-Art, for the check mark.</summary>
    public bool IsCurrent { get; }

    public IRelayCommand SelectCommand { get; }
}
