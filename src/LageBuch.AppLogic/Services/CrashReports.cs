using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The one artefact an unhandled exception leaves behind (#300). There is deliberately no log file:
/// failures surface on screen, and a log fed by every catch would hold the names and incident text
/// those catches see. A crash has no screen left to surface on, so it writes one small report
/// instead — app version, time, OS, and each exception's type and stack trace. Never a message:
/// an exception's text can carry a name, an address or a file path.
/// <para>
/// Written at crash time into a <c>pending</c> folder, then announced once on the next start by
/// <see cref="TakePending"/>, which moves it beside the older reports. A crash has the process going
/// down, so nothing is shown at that moment; the next start is the first that can.
/// </para>
/// </summary>
public static class CrashReports
{
    /// <summary>How many reports are kept; older ones are deleted when a new one is announced.</summary>
    public const int Kept = 5;

    private const string PendingFolder = "pending";

    // Deep enough for any real wrapping; a guard against a pathological cycle, not a limit anyone hits.
    private const int MaxDepth = 16;

    /// <summary>Where the reports of the app rooted at <paramref name="appRoot"/> are kept.</summary>
    public static string Folder(string appRoot) => Path.Join(appRoot, "crashes");

    /// <summary>
    /// Writes a report for <paramref name="exception"/>. Called from the unhandled-exception hooks,
    /// with the process going down, so it never throws.
    /// </summary>
    /// <returns><see langword="false"/> when the report could not be written.</returns>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Runs inside the unhandled-exception hook of a dying process: there is nothing left to surface to, and throwing would replace the original crash. The false return is the documented outcome.")]
    public static bool TryWrite(string appRoot, Exception exception, string appVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(appRoot);
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            var pending = Path.Join(Folder(appRoot), PendingFolder);
            Directory.CreateDirectory(pending);
            var name = "crash-" + now.UtcDateTime.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + ".txt";
            File.WriteAllText(Path.Join(pending, name), Format(exception, appVersion, now));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The newest report a crash left since the last start, moved out of <c>pending</c> so it is
    /// announced exactly once; <see langword="null"/> when there is none or it cannot be read.
    /// Best-effort and never throws.
    /// </summary>
    public static string? TakePending(string appRoot)
    {
        ArgumentNullException.ThrowIfNull(appRoot);
        var folder = Folder(appRoot);
        string? newest = null;
        try
        {
            // Names sort by time, so the last one moved is the newest.
            foreach (var file in Directory.GetFiles(Path.Join(folder, PendingFolder)).Order(StringComparer.Ordinal))
            {
                var destination = Path.Join(folder, Path.GetFileName(file));
                File.Move(file, destination, overwrite: true);
                newest = destination;
            }

            foreach (var old in Directory.GetFiles(folder).Order(StringComparer.Ordinal).SkipLast(Kept))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No folder yet (DirectoryNotFoundException is an IOException), or not ours to touch:
            // nothing to announce beyond what was already moved.
        }

        return newest;
    }

    internal static string Format(Exception exception, string appVersion, DateTimeOffset now)
    {
        var text = new StringBuilder()
            .AppendLine("Lagebuch " + appVersion)
            .AppendLine(now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            .AppendLine(RuntimeInformation.OSDescription);
        Append(text, exception, 0);
        return text.ToString();
    }

    private static void Append(StringBuilder text, Exception exception, int depth)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        text.AppendLine()
            .AppendLine(exception.GetType().FullName)
            .AppendLine(exception.StackTrace ?? "(kein Stacktrace)");

        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];
        foreach (var next in inner)
        {
            Append(text, next, depth + 1);
        }
    }
}
