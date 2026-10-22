using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One entry in a Beteiligte card's WOHNUNG flyout (#702): a Wohnung of the CO-Messung, or
/// <see cref="NoneDisplay"/> for "found in no recorded Wohnung".
/// </summary>
/// <remarks>
/// A command per entry rather than a ComboBox selection, for the reason given on
/// <see cref="SafetyTruppChoice"/>.
/// </remarks>
public sealed class DwellingChoice
{
    /// <summary>The label for "no Wohnung".</summary>
    public const string NoneDisplay = "— keine —";

    public DwellingChoice(Guid? id, string display, bool isCurrent, Action<Guid?> onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        Id = id;
        Display = display;
        IsCurrent = isCurrent;
        SelectCommand = new RelayCommand(() => onSelect(id));
    }

    /// <summary>The Wohnung, or null for "keine".</summary>
    public Guid? Id { get; }

    /// <summary>"Hauptstr. 12, 1. OG, Whg. 2".</summary>
    public string Display { get; }

    /// <summary>Whether this entry is the Wohnung currently recorded, for the check mark.</summary>
    public bool IsCurrent { get; }

    public IRelayCommand SelectCommand { get; }
}
