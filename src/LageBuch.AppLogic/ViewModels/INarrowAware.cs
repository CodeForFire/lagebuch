namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A module view model that lays out differently on a phone.
/// </summary>
/// <remarks>
/// Only <see cref="IncidentWorkspaceViewModel"/> sets this, and it sets it on every module it owns
/// the moment its own flag changes — so there is exactly one place in the app that decides what
/// "narrow" means, and exactly one size forwarder in the views feeding it.
/// <para>
/// This exists because the alternative does not work. Layout that is purely presentational —
/// padding, font size, a hidden label — is a container query in the .axaml and needs no view
/// model at all. But a phone also changes what the module <em>does</em>: an add-entry dock that is
/// always on screen becomes a sheet that opens and closes. A <c>Style</c> setter cannot express
/// that, and binding <c>IsVisible</c> to the open flag directly would hide the dock on the desktop
/// too, because a XAML binding is a local value and outranks every setter that might restore it.
/// A flag the view model can combine with its own state is the only thing that answers both.
/// </para>
/// </remarks>
public interface INarrowAware
{
    /// <summary>Whether this module is being laid out for a phone.</summary>
    bool IsNarrow { get; set; }
}
