namespace LageBuch.AppLogic.Services;

/// <summary>
/// Reduces an untrusted display name (e.g. a content provider's <c>DISPLAY_NAME</c>, which a
/// malicious provider fully controls) to a bare file name safe to <see cref="Path.Combine(string, string)"/>
/// into a fixed directory. Backslash-style traversal is normalised to forward slashes first so
/// <see cref="Path.GetFileName(string)"/> — which only recognises the current platform's own
/// separators — also strips a Windows-style <c>..\..\evil.dll</c> payload on Unix, not just
/// <c>../../evil.dll</c>.
/// </summary>
public static class SafeFileName
{
    private const string DefaultFallback = "anhang";

    public static string Sanitize(string? name, string fallback = DefaultFallback)
    {
        var normalized = (name ?? string.Empty).Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var invalidChars = Path.GetInvalidFileNameChars();
        var candidate = new string(fileName.Where(c => Array.IndexOf(invalidChars, c) < 0).ToArray());

        return candidate is "" or "." or ".." ? fallback : candidate;
    }
}
