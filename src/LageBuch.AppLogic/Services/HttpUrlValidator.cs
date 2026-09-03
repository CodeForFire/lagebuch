namespace LageBuch.AppLogic.Services;

/// <summary>
/// The http(s)-only URL check shared by every <see cref="IFileDialogService.OpenUrlAsync"/> caller
/// and implementation: a non-http(s) scheme (file://, javascript:, a bare local path treated as a
/// URI) must never reach a platform launcher, which on desktop shell-executes it (arbitrary URI
/// handlers, even local executables) and on Android hands it to <c>Intent.ActionView</c> (a known
/// intent-redirection surface for schemes like intent:// or content://).
/// </summary>
public static class HttpUrlValidator
{
    /// <summary>
    /// True only for an absolute, well-formed http or https URL. Does not attempt bare-domain
    /// normalization (e.g. "example.com" -&gt; "https://example.com") — that's a caller concern
    /// (see <c>LinksViewModel.OpenAsync</c>), not this validator's.
    /// </summary>
    public static bool TryGetHttpUri(string? input, out Uri uri)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var candidate) &&
            (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }
}
