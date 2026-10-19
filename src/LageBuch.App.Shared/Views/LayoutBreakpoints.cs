namespace LageBuch.App.Shared.Views;

/// <summary>
/// The one width at which this app stops being a desktop layout and becomes a phone one.
/// </summary>
/// <remarks>
/// The number is repeated as a literal in the views' <c>ContainerQuery</c> strings, because a
/// query is parsed from text and cannot read a constant. This class is what the code-behind
/// forwarders use, so the two halves of the same decision at least share one declared value to
/// point at; changing the breakpoint means changing this and grepping for <c>max-width:640</c>.
/// <para>
/// 640 is not a device width. It is the width the workspace header already chose when it started
/// dropping its tile labels (#485). A phone at 411dp in portrait sits below it, but the same phone
/// in landscape is 870–915dp wide and sits above it, alongside a tablet in portrait. Deliberately
/// not <c>OnFormFactor</c>: that resolves by device type at startup, so an Android
/// <em>tablet</em> — the form factor this app is actually built for — would get the phone layout.
/// </para>
/// <para>
/// The command bar in <c>MainView</c> does not fold here. Its contents need about 1017dp, so it
/// sheds in two stages, at widths of its own taken from what it measures: the brand descriptor
/// at 1040, the actions into MEHR at 880 (#648). <c>CommandBarReachabilityTests</c> sweeps every
/// width, so a button that outgrows those numbers fails the build instead of a tablet.
/// </para>
/// </remarks>
internal static class LayoutBreakpoints
{
    /// <summary>At or below this width, lay out for a phone.</summary>
    internal const double Narrow = 640;
}
