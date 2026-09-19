using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain.Atemschutz;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Trupp-Typ: its name, how many people it is crewed by, and the Einsatzzeit it
/// defaults to. The last two used to be decided by comparing the name against the compiled-in
/// literal "CSA-Trupp" (#398), which meant renaming the type here silently switched the rule off.
/// </summary>
public sealed partial class TruppTypeRow : ObservableObject
{
    private readonly Action _onChanged;

    public TruppTypeRow(string name, int memberCount, int maxDurationMinutes, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _memberCount = memberCount;
        _maxDurationMinutes = maxDurationMinutes;
    }

    /// <summary>Lowest crew the view's NumericUpDown offers -- a Trupp is never one person.</summary>
    public static int MinMemberCount => AtemschutzTrupp.StandardMemberCount;

    /// <summary>
    /// Highest crew the view's NumericUpDown offers. Bound rather than written into the .axaml so
    /// the control can never drift from the number of positions <c>TruppRole</c> actually has.
    /// </summary>
    public static int MaxMemberCount => AtemschutzTrupp.MaxMemberCount;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _memberCount;

    [ObservableProperty]
    private int _maxDurationMinutes;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnMemberCountChanged(int value) => _onChanged();

    partial void OnMaxDurationMinutesChanged(int value) => _onChanged();
}
