using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Services;

/// <summary>
/// Opens a new message in the operating system's mail program with the incident's PDF attached.
/// <c>mailto:</c> cannot carry an attachment, so each OS gets its own attaching route — Simple MAPI
/// on Windows, <c>xdg-email</c> on Linux, Mail.app via <c>osascript</c> on macOS — and when that
/// fails (no MAPI client, new Outlook, no <c>xdg-utils</c>) a plain <c>mailto:</c> opens instead and
/// the folder holding the PDF is shown, so attaching it is one drag away.
/// </summary>
internal sealed class OsMailComposer : IMailComposer
{
    // Some routes only return once the compose window closes -- classic Outlook behind MAPI,
    // xdg-email starting a Thunderbird that was not running yet. A launch still running after this
    // long has its window up; waiting for it would hold the export dialog's busy state hostage.
    private static readonly TimeSpan DefaultLaunchGrace = TimeSpan.FromSeconds(5);

    // Handed to osascript as a fixed script; subject, body and path arrive as argv, never as text
    // spliced into the script.
    private static readonly string[] MailAppScript =
    {
        "on run argv",
        "set theSubject to item 2 of argv",
        "set theBody to item 3 of argv",
        "set theFile to POSIX file (item 4 of argv)",
        "tell application \"Mail\"",
        "set theMessage to make new outgoing message with properties {subject:theSubject, content:theBody & return, visible:true}",
        "tell theMessage to make new attachment with properties {file name:theFile} at after the last paragraph",
        "activate",
        "end tell",
        "end run",
    };

    // What xdg-email's Thunderbird route would decode back out of an attachment path (IsSafeForXdgEmail).
    private static readonly char[] XdgEmailUnsafePathChars = { '\'', '"', ',', '\\', '%' };

    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> _run;
    private readonly TimeSpan _launchGrace;

    public OsMailComposer()
        : this(RunProcessAsync, DefaultLaunchGrace)
    {
    }

    // Seam for tests: records launches instead of starting processes.
    internal OsMailComposer(Func<ProcessStartInfo, CancellationToken, Task<int>> run, TimeSpan launchGrace)
    {
        _run = run;
        _launchGrace = launchGrace;
    }

    public bool CanCompose => true;

    public async Task<MailComposeResult> ComposeAsync(MailDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // Belt-and-suspenders like OpenFileAsync: whatever is attached leaves the device, so only
        // an existing PDF at an absolute path -- the one the export just wrote -- is handed on.
        if (!IsAttachablePdf(draft.AttachmentPath))
        {
            return MailComposeResult.Failed;
        }

        if (await TryAttachAsync(draft, cancellationToken))
        {
            return MailComposeResult.Attached;
        }

        if (!await TryLaunchAsync(MailtoLaunch(draft), cancellationToken))
        {
            return MailComposeResult.Failed;
        }

        // Best effort: the message is open either way, and the status line names the file.
        _ = await TryLaunchAsync(RevealLaunch(draft.AttachmentPath), cancellationToken);
        return MailComposeResult.OpenedWithoutAttachment;
    }

    internal static string BuildMailtoUri(MailDraft draft)
    {
        // RFC 6068: line breaks in a mailto body are CRLF, percent-encoded like everything else.
        var body = draft.Body.ReplaceLineEndings("\r\n");
        return $"mailto:?subject={Uri.EscapeDataString(draft.Subject)}&body={Uri.EscapeDataString(body)}";
    }

    // xdg-email consumes the word after each option as its value, so a value starting with "-" is
    // never read as an option of its own. It is a shell script, though, and pushes each value
    // through `echo` more than once; under dash every pass decodes backslash escapes again, so a
    // Stichwort carrying "\\0046to\\0075…" would come out as an extra recipient. A mail subject has
    // no use for a backslash, so none reaches it.
    internal static IReadOnlyList<string> BuildXdgEmailArguments(MailDraft draft) =>
        new[] { "--utf8", "--subject", NoBackslash(draft.Subject), "--body", NoBackslash(draft.Body), "--attach", draft.AttachmentPath };

    // run_thunderbird decodes the attach value with `echo -e` and splices it into attachment='…':
    // a quote, comma, backslash or percent sign in the file name would add compose fields of its
    // own (a bcc='…'). The name comes from the incident file, which someone else may have handed
    // over, so such a PDF takes the mailto: route instead of being rewritten behind the user's back.
    internal static bool IsSafeForXdgEmail(string path) => path.IndexOfAny(XdgEmailUnsafePathChars) < 0;

    private static string NoBackslash(string value) => value.Replace('\\', '/');

    // osascript stops reading options at its first plain argument, so the fixed "lagebuch" in front
    // keeps a subject such as "-e do shell script …" from being taken for a script of its own.
    internal static IReadOnlyList<string> BuildOsascriptArguments(MailDraft draft)
    {
        var args = new List<string>();
        foreach (var line in MailAppScript)
        {
            args.Add("-e");
            args.Add(line);
        }

        args.AddRange(new[] { "lagebuch", draft.Subject, draft.Body, draft.AttachmentPath });
        return args;
    }

    private static bool IsAttachablePdf(string path) =>
        Path.IsPathFullyQualified(path)
        && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    private static ProcessStartInfo Launch(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return psi;
    }

    // Windows resolves mailto: through its registered handler only via shell-execute; elsewhere the
    // desktop's opener does it, the same split as StorageProviderFileDialogService.LaunchWithOsDefault.
    // The URI is built here from the draft alone, with a fixed scheme.
    private static ProcessStartInfo MailtoLaunch(MailDraft draft)
    {
        var uri = BuildMailtoUri(draft);
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(uri) { UseShellExecute = true };
        }

        return Launch(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { uri });
    }

    private static ProcessStartInfo RevealLaunch(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // explorer.exe parses its own command line and takes no ArgumentList; the path is an
            // existing absolute file (IsAttachablePdf), and '"' cannot occur in a Windows path.
            return new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false };
        }

        return OperatingSystem.IsMacOS()
            ? Launch("open", new[] { "-R", path })
            : Launch("xdg-open", new[] { Path.GetDirectoryName(path) ?? path });
    }

    private async Task<bool> TryAttachAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var send = MapiMail.SendAsync(draft);
            return await WithinGraceAsync(() => send, MapiMail.IsHandled, cancellationToken);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await TryLaunchAsync(Launch("osascript", BuildOsascriptArguments(draft)), cancellationToken);
        }

        return IsSafeForXdgEmail(draft.AttachmentPath)
            && await TryLaunchAsync(Launch("xdg-email", BuildXdgEmailArguments(draft)), cancellationToken);
    }

    private Task<bool> TryLaunchAsync(ProcessStartInfo launch, CancellationToken cancellationToken) =>
        WithinGraceAsync(() => _run(launch, cancellationToken), exitCode => exitCode == 0, cancellationToken);

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A launcher that is missing or throws (no xdg-utils, no mail handler, no MAPI client) is exactly what the caller falls back from; the false return is the surface.")]
    private async Task<bool> WithinGraceAsync(Func<Task<int>> launch, Func<int, bool> succeeded, CancellationToken cancellationToken)
    {
        try
        {
            return succeeded(await launch().WaitAsync(_launchGrace, cancellationToken));
        }
        catch (TimeoutException)
        {
            return true; // still running: the compose window is open
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<int> RunProcessAsync(ProcessStartInfo launch, CancellationToken cancellationToken)
    {
        using var process = Process.Start(launch);
        if (process is null)
        {
            return 0; // shell-execute handed the URI to an already running handler
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
