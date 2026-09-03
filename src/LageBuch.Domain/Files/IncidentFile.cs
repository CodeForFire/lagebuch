namespace LageBuch.Domain.Files;

public sealed record IncidentFile
{
    // Base64 over a single POST inflates a binary payload ~33%, so 25 MB stays a one-shot,
    // few-second transfer on the app's Tailscale-LAN sync path while comfortably covering a
    // phone photo or a scanned PDF.
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

        var trimmedName = fileName.Trim();
        return new IncidentFile
        {
            Id = id,
            FileName = trimmedName,
            DisplayName = trimmedName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            AddedAt = addedAt,
            AddedBy = addedBy,
        };
    }

    public static IncidentFile Rehydrate(
        Guid id, string fileName, string displayName, string contentType, long sizeBytes, DateTimeOffset addedAt, string addedBy)
        => new()
        {
            Id = id,
            FileName = fileName,
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
}
