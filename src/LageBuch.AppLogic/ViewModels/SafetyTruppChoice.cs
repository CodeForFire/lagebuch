using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One entry in a row's Sicherheitstrupp flyout (#399): a Trupp that may be designated, or
/// <see cref="NoneDisplay"/> for "no Sicherheitstrupp".
/// </summary>
/// <remarks>
/// Each entry carries its own command, so the flyout's item template binds against the entry
/// itself and needs no <c>$parent</c> traversal out of the popup's separate visual tree.
/// <para>
/// This is deliberately a command and not a ComboBox selection. Binding <c>SelectedItem</c> two-way
/// to a setter that writes to the domain crashed the app: the write re-enters through the incident's
/// Changed event and alters the same collection Avalonia is using as its ItemsSource, while the
/// selection model is still mid-<c>BatchUpdateOperation</c> holding indices into it. A command runs
/// after the flyout dismisses, with no selection model involved at all.
/// </para>
/// </remarks>
public sealed class SafetyTruppChoice
{
    /// <summary>The label for the "no Sicherheitstrupp designated" entry.</summary>
    public const string NoneDisplay = "— kein —";

    public SafetyTruppChoice(Guid? id, string display, string? detail, bool isCurrent, Action<Guid?> onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        Id = id;
        Display = display;
        Detail = detail;
        IsCurrent = isCurrent;
        SelectCommand = new RelayCommand(() => onSelect(id));
    }

    /// <summary>The Trupp being designated, or null for "kein Sicherheitstrupp".</summary>
    public Guid? Id { get; }

    /// <summary>The short label, e.g. "Trupp 3" — the full DisplayName does not fit the cell.</summary>
    public string Display { get; }

    /// <summary>The Trupp-Art, shown dimmed beside the label, or null for the "kein" entry.</summary>
    public string? Detail { get; }

    /// <summary>Whether this entry is the one currently in force, for the check mark.</summary>
    public bool IsCurrent { get; }

    public IRelayCommand SelectCommand { get; }
}
