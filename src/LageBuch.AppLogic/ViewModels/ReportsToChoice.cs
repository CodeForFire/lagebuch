using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One entry in a Funktion row's MELDET AN flyout (#703): a running assignment the row may report
/// to, or <see cref="NoneDisplay"/> for "reports to no one recorded here".
/// </summary>
/// <remarks>
/// A command per entry rather than a ComboBox selection, for the reason given on
/// <see cref="SafetyTruppChoice"/>: the write comes straight back through Changed and rebuilds the
/// list a selection model would still be holding indices into.
/// </remarks>
public sealed class ReportsToChoice
{
    /// <summary>The label for "reports to no one".</summary>
    public const string NoneDisplay = "— niemand —";

    /// <summary>The label for a link to an assignment this incident does not hold (an older or foreign file).</summary>
    public const string UnknownDisplay = "unbekannt";

    public ReportsToChoice(Guid? id, string display, string? detail, bool isCurrent, Action<Guid?> onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        Id = id;
        Display = display;
        Detail = detail;
        IsCurrent = isCurrent;
        SelectCommand = new RelayCommand(() => onSelect(id));
    }

    /// <summary>The assignment reported to, or null for "niemand".</summary>
    public Guid? Id { get; }

    /// <summary>"EL (Huber)": the Funktion and who holds it.</summary>
    public string Display { get; }

    /// <summary>The Abschnitt, shown dimmed beside the label, or null.</summary>
    public string? Detail { get; }

    /// <summary>Whether this entry is the link currently in force, for the check mark.</summary>
    public bool IsCurrent { get; }

    public IRelayCommand SelectCommand { get; }
}
