using LageBuch.Domain.Files;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Reduces an untrusted display name (e.g. a content provider's <c>DISPLAY_NAME</c>, which a
/// malicious provider fully controls) to a bare file name safe to <see cref="Path.Join(string, string)"/>
/// into a fixed directory.
/// <para>
/// A thin alias for <see cref="FileNameSanitizer"/>, which attachment names already went through —
/// the two used to be separate near-duplicates with subtly different rules. Kept as its own name
/// because the Android head's concern reads as "give me something usable" rather than "tell me if
/// this name survives", which is the distinction <see cref="FileNameSanitizer.TrySanitize"/> draws.
/// </para>
/// </summary>
public static class SafeFileName
{
    /// <summary>The fallback name used when sanitising leaves nothing usable behind.</summary>
    public const string DefaultFallback = FileNameSanitizer.DefaultFallback;

    public static string Sanitize(string? name, string fallback = DefaultFallback) =>
        FileNameSanitizer.Sanitize(name, fallback);
}
