using System.Globalization;
using System.Text;

namespace LageBuch.Domain.Files;

/// <summary>
/// The one place an untrusted name is reduced to a bare, traversal-free file name. Two kinds of
/// name arrive here and neither can be trusted: an attachment name a joined client picks in
/// <c>AddFileCommand</c>, which every peer later writes bytes under before handing the file to the
/// OS; and a content provider's <c>DISPLAY_NAME</c> on Android, which a malicious provider fully
/// controls. Both end up in a <see cref="Path.Join(string, string)"/> against a fixed directory,
/// so <c>..\..\Startup\x.png</c> must never survive: only the last path segment is kept.
/// <para>
/// Callers decide what "nothing usable is left" means — <see cref="IncidentFile.Create(Guid, string, string, long, DateTimeOffset, string)"/>
/// throws, <c>IncidentFile.Rehydrate</c> falls back to the storage-style name so an existing
/// attachment still opens, and <c>SafeFileName</c> substitutes <see cref="DefaultFallback"/>. This
/// only reports that nothing is left, by returning <c>null</c>.
/// </para>
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>The name used where a caller wants a usable one rather than a failure.</summary>
    public const string DefaultFallback = "anhang";

    /// <summary>
    /// The per-name byte budget virtually every filesystem enforces (ext4, APFS, NTFS components).
    /// A longer name would fail the write on a peer that opens the file, so it is capped here, once,
    /// where every device sees the same result.
    /// </summary>
    private const int MaxFileNameBytes = 255;

    /// <summary>
    /// Characters that must not appear in a file name. The OS-specific set is only a starting point:
    /// on Linux it is just <c>\0</c> and <c>/</c>, so the Windows-invalid set is added
    /// unconditionally — the same attachment travels between a Windows host and a Linux client (and
    /// back) and has to end up with the same name on both.
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
        [.. Path.GetInvalidFileNameChars(), .. "<>:\"|?*\\/"];

    /// <summary>Both path separators, whatever the platform thinks of them.</summary>
    private static readonly char[] SeparatorChars = ['/', '\\'];

    /// <summary>
    /// Reduces <paramref name="fileName"/> to a bare file name, or returns <c>null</c> when nothing
    /// usable is left.
    /// <para>
    /// Deliberately hand-rolled rather than <see cref="Path.GetFileName(string)"/>, whose idea of a
    /// separator is the platform's: a Windows host and a Linux client sync the same name and must
    /// end up with the same file, so both separators (and the Windows-invalid character set) apply
    /// everywhere.
    /// </para>
    /// <para>
    /// The result is capped at 255 UTF-8 bytes by shortening the stem and keeping the extension,
    /// which is what decides the viewer a peer's ÖFFNEN launches.
    /// </para>
    /// </summary>
    public static string? TrySanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var trimmed = fileName.Trim();
        var lastSegment = trimmed[(trimmed.LastIndexOfAny(SeparatorChars) + 1)..];
        var cleaned = new string(lastSegment
            .Where(c => !IsHidden(c) && Array.IndexOf(InvalidFileNameChars, c) < 0)
            .ToArray()).Trim();

        // "." and ".." are directory references, not names — and an all-dots name is no better.
        return cleaned.Length == 0 || cleaned.All(c => c == '.') ? null : CapToByteLimit(cleaned);
    }

    /// <summary>
    /// <see cref="TrySanitize"/> with <paramref name="fallback"/> substituted when nothing usable is
    /// left, for callers that must always produce a name.
    /// </summary>
    public static string Sanitize(string? fileName, string fallback = DefaultFallback) =>
        TrySanitize(fileName) ?? fallback;

    /// <summary>
    /// Characters that occupy no width of their own, so they cannot be seen in a rendered file name
    /// but can still change how it reads. <see cref="char.IsControl(char)"/> alone misses the
    /// bidirectional overrides (U+202E and friends), which are <see cref="UnicodeCategory.Format"/>:
    /// they are invisible but reorder what follows, so <c>"Lageplan‮gnp.exe"</c> reads as
    /// <c>Lageplan exe.png</c> while still being an <c>.exe</c>. This also removes the zero-width
    /// joiners, which no file name needs. (#302)
    /// </summary>
    private static bool IsHidden(char c) =>
        char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;

    /// <summary>
    /// Shortens the stem until the whole name fits <see cref="MaxFileNameBytes"/> UTF-8 bytes,
    /// keeping the extension. Cuts on a character boundary (the encoder stops at the last whole
    /// one), so a multi-byte name never ends in half a character.
    /// </summary>
    private static string CapToByteLimit(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxFileNameBytes)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var extensionBytes = Encoding.UTF8.GetByteCount(extension);
        if (extensionBytes >= MaxFileNameBytes)
        {
            // Pathological: an "extension" that fills the budget on its own leaves no stem to keep.
            extension = string.Empty;
            extensionBytes = 0;
        }

        var stem = name[..^extension.Length];
        var buffer = new byte[MaxFileNameBytes - extensionBytes];

        // flush: false — a trailing high surrogate that has no room for its pair stays unconsumed
        // rather than being encoded as a replacement character.
        Encoding.UTF8.GetEncoder().Convert(stem, buffer, flush: false, out var charsUsed, out _, out _);
        return string.Concat(stem.AsSpan(0, charsUsed), extension);
    }
}
