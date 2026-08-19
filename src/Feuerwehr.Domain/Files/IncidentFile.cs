namespace Feuerwehr.Domain.Files;

public sealed record IncidentFile
{
    // Base64 over a single POST inflates a binary payload ~33%, so 25 MB stays a one-shot,
    // few-second transfer on the app's Tailscale-LAN sync path while comfortably covering a
    // phone photo or a scanned PDF.
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf"
    };

    private IncidentFile() { }

    public Guid Id { get; private init; }
    public string FileName { get; private init; } = string.Empty;
    public string DisplayName { get; private init; } = string.Empty;
    public string ContentType { get; private init; } = string.Empty;
    public long SizeBytes { get; private init; }
    public DateTimeOffset AddedAt { get; private init; }
    public string AddedBy { get; private init; } = string.Empty;

    public static IncidentFile Create(
        string fileName, string contentType, long sizeBytes, DateTimeOffset addedAt, string addedBy)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Dateiname darf nicht leer sein.", nameof(fileName));
        if (!AllowedContentTypes.Contains(contentType))
            throw new ArgumentException($"Dateityp '{contentType}' wird nicht unterstützt.", nameof(contentType));
        if (sizeBytes <= 0)
            throw new ArgumentException("Dateigröße muss positiv sein.", nameof(sizeBytes));
        if (sizeBytes > MaxSizeBytes)
            throw new ArgumentException($"Datei ist größer als das Limit von {MaxSizeBytes / (1024 * 1024)} MB.", nameof(sizeBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(addedBy);

        var trimmedName = fileName.Trim();
        return new IncidentFile
        {
            Id = Guid.NewGuid(),
            FileName = trimmedName,
            DisplayName = trimmedName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            AddedAt = addedAt,
            AddedBy = addedBy
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
            AddedBy = addedBy
        };

    /// <summary>
    /// A freely-editable label shown in the Dateien list and the PDF export, independent of
    /// <see cref="FileName"/> (which stays fixed — it drives the storage extension and the
    /// temp-file name "Öffnen" hands to the OS). Clearing the field resets to <see cref="FileName"/>
    /// rather than persisting a blank label.
    /// </summary>
    public IncidentFile WithDisplayName(string? displayName) => this with
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? FileName : displayName.Trim()
    };

    /// <summary>
    /// The name this file's bytes are stored under (sibling <c>.files</c> folder, or the
    /// host's <c>GET /files/{id}</c> cache key on a joined client) — always derived from
    /// <see cref="Id"/> and the original extension, never persisted separately.
    /// </summary>
    public static string StorageFileName(Guid id, string fileName) => $"{id}{Path.GetExtension(fileName)}";
}
