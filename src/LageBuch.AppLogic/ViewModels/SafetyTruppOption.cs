namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One entry in a row's Sicherheitstrupp picker (#399). A record rather than a class so two
/// instances built by two row rebuilds compare equal and the ComboBox keeps its selection;
/// "kein Sicherheitstrupp" is <see cref="None"/>, an option carrying a null <see cref="Id"/>,
/// rather than a null item — which is what lets a row treat a null selection as the ComboBox
/// resetting itself and never as a user choice.
/// </summary>
/// <param name="Id">The Trupp being designated, or null for "kein Sicherheitstrupp".</param>
/// <param name="Display">The short label, e.g. "Trupp 3" — the full DisplayName does not fit the
/// column.</param>
/// <param name="Detail">The Trupp-Art, shown dimmed beside the label, or null for
/// <see cref="None"/>.</param>
public sealed record SafetyTruppOption(Guid? Id, string Display, string? Detail)
{
    /// <summary>The "no Sicherheitstrupp designated" entry, always first in the list.</summary>
    public static readonly SafetyTruppOption None = new(null, "— kein —", null);
}
