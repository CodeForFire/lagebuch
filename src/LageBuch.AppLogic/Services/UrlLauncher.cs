using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Opens an http(s) URL through <see cref="IFileDialogService.OpenUrlAsync"/> and turns either
/// failure — a URL that is not http(s), or a platform launcher that throws — into the German
/// message a view model shows in place. The companion to <see cref="HttpUrlValidator"/>, which
/// decides what may be launched; this decides what the user is told when it cannot be.
/// </summary>
public static class UrlLauncher
{
    /// <summary>
    /// Returns <c>null</c> when the URL was handed to the platform launcher, or the message to show
    /// otherwise. Returning the message rather than setting it keeps the "which property, and on
    /// which view model" decision with the caller.
    /// </summary>
    /// <param name="dialogs">The platform launcher.</param>
    /// <param name="url">The candidate URL. Bare-domain normalization is the caller's job — see
    /// <see cref="HttpUrlValidator.TryGetHttpUri"/>.</param>
    /// <param name="label">What to name in the message: the URL itself where that is what the user
    /// sees (the About dialog), the link's display name where the URL is not on screen.</param>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Deliberately broad: any launcher failure surfaces in the view instead of crashing it.")]
    [SuppressMessage(
        "Design",
        "CA1054",
        Justification = "A string is the input by design: deciding whether an unvalidated, possibly malformed string is a usable http(s) URL is this method's job, so requiring a parsed System.Uri would push that decision back onto every caller.")]
    public static async Task<string?> TryOpenAsync(IFileDialogService dialogs, string? url, string label)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        if (!HttpUrlValidator.TryGetHttpUri(url, out var uri))
        {
            return $"„{label}“ hat keine gültige http(s)-Adresse.";
        }

        try
        {
            await dialogs.OpenUrlAsync(uri.AbsoluteUri);
            return null;
        }
        catch (Exception ex)
        {
            // No browser/URL handler registered on this machine (a minimal offline install, or no
            // app on Android able to resolve Intent.ActionView) throws out of the platform launcher;
            // report it in place instead of crashing the view.
            return $"„{label}“ konnte nicht geöffnet werden: {ex.Message}";
        }
    }
}
