using System.Diagnostics.CodeAnalysis;
using System.Text;
using LageBuch.Domain.Time;

namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// Finds the newest unanswered alarm in the alarm folder. The ELW's PC is switched on at the
/// Einsatzort, so the mail is usually minutes old by the time anything looks: the scanner looks
/// back <see cref="MaxAge"/>, and anything older is ignored without a word.
/// <para>
/// The folder is polled rather than watched. It is typically a OneDrive folder that syncs while
/// the PC boots, whose placeholders and late writes do not raise file-system events reliably. The
/// files are only ever read: whatever put them there owns them.
/// </para>
/// </summary>
public sealed class DispatchFolderScanner
{
    /// <summary>The largest file read; a real alarm mail is around 25 KB.</summary>
    public const long MaxFileBytes = FfAgentDispatchParser.MaxContentLength;

    /// <summary>The most files read in one scan, newest first.</summary>
    public const int MaxFiles = 200;

    /// <summary>How old an alarm may be and still be offered.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    // A PC clock a few minutes behind the Leitstelle's must not make a fresh alarm look future.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(15);

    private static readonly string[] Extensions = [".html", ".htm", ".xml", ".eml"];

    private readonly IClock _clock;
    private readonly TimeZoneInfo _zone;

    public DispatchFolderScanner(IClock clock, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
    }

    public DispatchScan Scan(string folder, IReadOnlySet<string> handled)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(handled);
        var now = _clock.Now;

        List<FileInfo> candidates;
        try
        {
            candidates = new DirectoryInfo(folder)
                .EnumerateFiles()
                .Where(f => Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && IsPlainFile(f))
                .Where(f => f.Length <= MaxFileBytes && IsRecent(f.LastWriteTimeUtc, now))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxFiles)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new DispatchScan(null, $"Alarm-Ordner nicht lesbar: {folder}");
        }

        DispatchAlarm? newest = null;
        DateTimeOffset newestAt = default;
        foreach (var file in candidates)
        {
            if (!TryRead(file, out var content)
                || !TryParse(content, out var alarm)
                || handled.Contains(alarm.Key))
            {
                continue;
            }

            var at = alarm.AlarmedAt ?? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (IsRecent(at, now) && (newest is null || at > newestAt))
            {
                newest = alarm;
                newestAt = at;
            }
        }

        return new DispatchScan(newest, null);
    }

    /// <summary>
    /// The alarm in one file picked by hand, read under the same rules as the folder (a plain
    /// file, size-capped) but whatever its age and whether or not it was answered before; false
    /// when the file is unreadable or holds no alarm.
    /// </summary>
    public bool TryReadFile(string path, [NotNullWhen(true)] out DispatchAlarm? alarm)
    {
        ArgumentNullException.ThrowIfNull(path);
        alarm = null;
        var file = new FileInfo(path);
        return file.Exists
            && IsPlainFile(file)
            && file.Length <= MaxFileBytes
            && TryRead(file, out var content)
            && TryParse(content, out alarm);
    }

    // The parser refuses what it knows to be no alarm. Should a file still find a way to make it
    // throw, that one file is passed over: a single hostile file must not hide every other alarm
    // in the folder for as long as it lies there.
    [SuppressMessage("Design", "CA1031", Justification = "A file that breaks the parser is no alarm; it is skipped and the others are still read.")]
    private bool TryParse(string content, [NotNullWhen(true)] out DispatchAlarm? alarm)
    {
        try
        {
            return FfAgentDispatchParser.TryParse(content, _zone, out alarm);
        }
        catch (Exception)
        {
            alarm = null;
            return false;
        }
    }

    private static bool IsRecent(DateTimeOffset at, DateTimeOffset now) =>
        at >= now - MaxAge && at <= now + ClockSkew;

    private static bool IsRecent(DateTime utc, DateTimeOffset now) =>
        IsRecent(new DateTimeOffset(utc, TimeSpan.Zero), now);

    // A link or a device reports its own size, not what reading it yields: a link to /dev/zero is
    // "0 bytes" and never ends. Only plain files are alarm mails.
    private static bool IsPlainFile(FileInfo file) =>
        file.LinkTarget is null && (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;

    // A file still being written by the sync client, or locked by it, is read on the next poll
    // a few seconds later; until then it is simply not an alarm yet.
    private static bool TryRead(FileInfo file, [NotNullWhen(true)] out string? content)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            return TryReadCapped(stream, out content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            content = null;
            return false;
        }
    }

    /// <summary>
    /// Reads <paramref name="stream"/> as text, or refuses it once it runs past
    /// <see cref="MaxFileBytes"/>. The size listed for the file was checked before, but a file can
    /// grow between that look and this read, so the cap is held here as well.
    /// </summary>
    internal static bool TryReadCapped(Stream stream, [NotNullWhen(true)] out string? content)
    {
        var buffer = new byte[MaxFileBytes + 1];
        var length = 0;
        var read = -1;
        while (length < buffer.Length && read != 0)
        {
            read = stream.Read(buffer, length, buffer.Length - length);
            length += read;
        }

        if (length > MaxFileBytes)
        {
            content = null;
            return false;
        }

        using var bytes = new MemoryStream(buffer, 0, length);
        using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        content = reader.ReadToEnd();
        return true;
    }
}
