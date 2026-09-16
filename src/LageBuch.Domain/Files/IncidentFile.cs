using System.Text;

namespace LageBuch.Domain.Files;

public sealed record IncidentFile
{
    // Uploads stream straight to disk on the host (issue #167 P1 #2) without ever holding a
    // whole-file byte[] there, but the client still reads the picked file fully into memory
    // before sending it (FilesViewModel.AddFileAsync) and fully buffers a downloaded file too
    // (RemoteIncidentSession.GetFileBytesAsync) — so this cap bounds that client-side allocation,
    // not wire-transfer size. 25 MB keeps that a one-shot, few-second transfer on the app's
    // Tailscale-LAN sync path while comfortably covering a phone photo or a scanned PDF.
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    /// <summary>
    /// The single extension→MIME table every allowlist and every path-to-content-type mapping in
    /// the app derives from (picker filters, upload validation, Android share intents) — see
    /// <see cref="GetMimeType"/>. Keying and lookup are case-insensitive.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MimeTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf",
        };

    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(MimeTypesByExtension.Values, StringComparer.OrdinalIgnoreCase);

    /// <summary>The generic fallback used where "unknown" should still mean a concrete, valid MIME
    /// type rather than Android's wildcard intent type — see <see cref="GetMimeType"/>.</summary>
    public const string DefaultMimeType = "application/octet-stream";

    /// <summary>
    /// Maps a local file path's extension to the MIME type <see cref="MimeTypesByExtension"/>
    /// knows, or <paramref name="fallback"/> when the extension is unrecognized. Callers pass their
    /// own fallback because "unknown" means different things to different consumers — a concrete
    /// generic type (<see cref="DefaultMimeType"/>) versus an Android intent wildcard (<c>*/*</c>).
    /// </summary>
    public static string GetMimeType(string path, string fallback) =>
        MimeTypesByExtension.TryGetValue(Path.GetExtension(path), out var mimeType) ? mimeType : fallback;

    private IncidentFile()
    {
    }

    public Guid Id { get; private init; }

    public string FileName { get; private init; } = string.Empty;

    public string DisplayName { get; private init; } = string.Empty;

    public string ContentType { get; private init; } = string.Empty;

    public long SizeBytes { get; private init; }

    public DateTimeOffset AddedAt { get; private init; }

    public string AddedBy { get; private init; } = string.Empty;

    public static IncidentFile Create(
        string fileName, string contentType, long sizeBytes, DateTimeOffset addedAt, string addedBy) =>
        Create(Guid.NewGuid(), fileName, contentType, sizeBytes, addedAt, addedBy);

    /// <summary>
    /// Overload taking an externally-supplied id (issue #167 P1 #2): the client generates the file id
    /// up front so it can correlate the metadata command it sends with the raw-byte upload that
    /// follows, before the domain has ever seen this file.
    /// </summary>
    public static IncidentFile Create(
        Guid id, string fileName, string contentType, long sizeBytes, DateTimeOffset addedAt, string addedBy)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Dateiname darf nicht leer sein.", nameof(fileName));
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new ArgumentException($"Dateityp '{contentType}' wird nicht unterstützt.", nameof(contentType));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException("Dateigröße muss positiv sein.", nameof(sizeBytes));
        }

        if (sizeBytes > MaxSizeBytes)
        {
            throw new ArgumentException($"Datei ist größer als das Limit von {MaxSizeBytes / (1024 * 1024)} MB.", nameof(sizeBytes));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(addedBy);

        var safeName = SanitizeFileName(fileName)
            ?? throw new ArgumentException($"Dateiname '{fileName}' ist ungültig.", nameof(fileName));

        // The extension — not the declared content type — is what the OS launches when a peer
        // presses ÖFFNEN, so the two must agree and the extension must be one we allow. Otherwise a
        // client could declare "Lageplan.hta" as image/png and have every peer feed the bytes to
        // mshta (.js/.wsf/.lnk/.vbs likewise, .desktop on Linux).
        if (!MimeTypesByExtension.TryGetValue(Path.GetExtension(safeName), out var mappedType)
            || !string.Equals(mappedType, contentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Dateiendung von '{safeName}' passt nicht zum Dateityp '{contentType}'.", nameof(fileName));
        }

        return new IncidentFile
        {
            Id = id,
            FileName = safeName,
            DisplayName = safeName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            AddedAt = addedAt,
            AddedBy = addedBy,
        };
    }

    /// <summary>
    /// The load path (SQLite, and a host's snapshot via <c>SnapshotMapper.FromSnapshot</c>), so it
    /// sanitises exactly like <c>Create</c> — a hostile name written by an older or patched
    /// peer is neutralised on the way in — but never throws: an existing attachment must still
    /// open, so a name with nothing usable left falls back to the storage-style
    /// <c>{id}{extension}</c>. <paramref name="displayName"/> is a free-form label that never
    /// reaches the filesystem and is kept verbatim.
    /// </summary>
    public static IncidentFile Rehydrate(
        Guid id, string fileName, string displayName, string contentType, long sizeBytes, DateTimeOffset addedAt, string addedBy)
        => new()
        {
            Id = id,
            FileName = SanitizeFileName(fileName)
                ?? SanitizeFileName(StorageFileName(id, fileName))
                ?? FallbackFileName,
            DisplayName = displayName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            AddedAt = addedAt,
            AddedBy = addedBy,
        };

    /// <summary>
    /// A freely-editable label shown in the Dateien list and the PDF export, independent of
    /// <see cref="FileName"/> (which stays fixed — it drives the storage extension and the
    /// temp-file name "Öffnen" hands to the OS). Clearing the field resets to <see cref="FileName"/>
    /// rather than persisting a blank label.
    /// </summary>
    public IncidentFile WithDisplayName(string? displayName) => this with
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? FileName : displayName.Trim(),
    };

    /// <summary>
    /// The name this file's bytes are stored under (sibling <c>.files</c> folder, or the
    /// host's <c>GET /files/{id}</c> cache key on a joined client) — always derived from
    /// <see cref="Id"/> and the original extension, never persisted separately.
    /// </summary>
    public static string StorageFileName(Guid id, string fileName) => $"{id}{Path.GetExtension(fileName)}";

    /// <summary>Last resort when not even the storage-style name survives sanitising.</summary>
    private const string FallbackFileName = "anhang";

    /// <summary>
    /// The per-name byte budget virtually every filesystem enforces (ext4, APFS, NTFS components).
    /// A longer name would fail the write on a peer that opens the file, so it is capped here, once,
    /// where every device sees the same result.
    /// </summary>
    private const int MaxFileNameBytes = 255;

    /// <summary>
    /// Characters that must not appear in a <see cref="FileName"/>. The OS-specific set is only a
    /// starting point: on Linux it is just <c>\0</c> and <c>/</c>, so the Windows-invalid set is
    /// added unconditionally — the same attachment travels between a Windows host and a Linux
    /// client (and back) and has to end up with the same name on both.
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
        [.. Path.GetInvalidFileNameChars(), .. "<>:\"|?*\\/"];

    /// <summary>Both path separators, whatever the platform thinks of them.</summary>
    private static readonly char[] SeparatorChars = ['/', '\\'];

    /// <summary>
    /// Reduces an attachment name to a bare, traversal-free file name, or returns <c>null</c> when
    /// nothing usable is left. Attachment names are attacker-controlled — a joined client picks the
    /// name in <c>AddFileCommand</c> and every peer later writes those bytes to a temp file under
    /// that name before handing it to the OS — so a name like <c>..\..\Startup\x.png</c> must never
    /// survive: only the last path segment is kept.
    /// <para>
    /// Deliberately hand-rolled rather than <see cref="Path.GetFileName(string)"/>, whose idea of a
    /// separator is the platform's: a Windows host and a Linux client sync the same name and must
    /// end up with the same file, so both separators (and the Windows-invalid character set) apply
    /// everywhere.
    /// </para>
    /// <para>
    /// The result is capped at <see cref="MaxFileNameBytes"/> UTF-8 bytes — the limit almost every
    /// filesystem enforces — by shortening the stem and keeping the extension, which is what
    /// decides the viewer a peer's ÖFFNEN launches.
    /// </para>
    /// </summary>
    private static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var trimmed = fileName.Trim();
        var lastSegment = trimmed[(trimmed.LastIndexOfAny(SeparatorChars) + 1)..];
        var cleaned = new string(lastSegment
            .Where(c => !char.IsControl(c) && Array.IndexOf(InvalidFileNameChars, c) < 0)
            .ToArray()).Trim();

        // "." and ".." are directory references, not names — and an all-dots name is no better.
        return cleaned.Length == 0 || cleaned.All(c => c == '.') ? null : CapToByteLimit(cleaned);
    }

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
