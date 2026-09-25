using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The read/deserialise/write scaffolding every one of the app's small JSON preference files shares
/// (recent incidents, last save folder, last PDF export, last connection). Each of those stores owns
/// its own semantics — what "empty" means, how a value is folded into the previous one — and leaves
/// the file handling here.
/// <para>
/// A missing or corrupt file reads as "no value": these files are conveniences, and a truncated
/// <c>recent.json</c> must never stop the app from starting. Only <see cref="JsonException"/> is
/// swallowed — an <see cref="IOException"/> still surfaces, because a file we cannot read at all is
/// a different problem from one holding nothing useful.
/// </para>
/// </summary>
/// <typeparam name="T">The payload persisted as the file's entire contents.</typeparam>
internal sealed class JsonFileStore<T>
{
    private readonly string _path;

    public JsonFileStore(string path) => _path = path;

    /// <summary>
    /// Reads the stored payload, or returns <c>false</c> when the file is missing, unparseable, or
    /// holds a JSON <c>null</c>. The out-parameter shape rather than a <c>T?</c> return is what lets
    /// a value-type payload (<see cref="LastPdfExport"/>) distinguish "nothing stored" from a
    /// default-valued record.
    /// </summary>
    public bool TryRead([MaybeNullWhen(false)] out T value)
    {
        value = default;
        if (!File.Exists(_path))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return false;
        }

        return value is not null;
    }

    /// <summary>
    /// Write-then-rename (the atomicity #286 gave the trust store): a crash between the two leaves
    /// either the old file untouched or the new one fully in place — never a half-written file that
    /// the next read would have to discard. <c>overwrite: true</c> also clobbers a stale <c>*.tmp</c>
    /// left behind by a process that crashed between the write and the move on a previous attempt.
    /// </summary>
    public void Write(T value)
    {
        var tmpPath = _path + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(value));
        File.Move(tmpPath, _path, overwrite: true);
    }

    /// <summary>
    /// Removes the stored value, and any <c>*.tmp</c> a crashed <see cref="Write"/> left behind, so
    /// nothing of it stays on disk. A missing file is not an error: there is nothing to remove.
    /// </summary>
    public void Delete()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }
}
