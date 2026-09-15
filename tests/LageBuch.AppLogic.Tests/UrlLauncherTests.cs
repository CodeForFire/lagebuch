using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class UrlLauncherTests
{
    [Fact]
    public async Task A_launched_url_reports_no_error()
    {
        var dialogs = new RecordingDialogs();

        var error = await UrlLauncher.TryOpenAsync(dialogs, "https://example.org/lage", "Lagekarte");

        Assert.Null(error);
        Assert.Equal("https://example.org/lage", dialogs.OpenedUrl);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.org")]
    [InlineData("example.org")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_non_http_url_never_reaches_the_launcher(string? candidate)
    {
        var dialogs = new RecordingDialogs();

        var error = await UrlLauncher.TryOpenAsync(dialogs, candidate, "Lagekarte");

        Assert.Equal("„Lagekarte“ hat keine gültige http(s)-Adresse.", error);
        Assert.Null(dialogs.OpenedUrl);
    }

    [Fact]
    public async Task A_throwing_launcher_is_reported_rather_than_crashing_the_caller()
    {
        var error = await UrlLauncher.TryOpenAsync(new ThrowingDialogs(), "https://example.org", "Lagekarte");

        Assert.NotNull(error);
        Assert.Contains("Lagekarte", error, StringComparison.Ordinal);
        Assert.Contains("kein Browser gefunden", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_label_names_what_the_user_sees_not_the_url()
    {
        var error = await UrlLauncher.TryOpenAsync(new ThrowingDialogs(), "https://example.org/sehr/lang", "Lagekarte");

        Assert.NotNull(error);
        Assert.DoesNotContain("example.org", error, StringComparison.Ordinal);
    }

    private class RecordingDialogs : IFileDialogService
    {
        public string? OpenedUrl { get; private set; }

        public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public virtual Task OpenUrlAsync(string url)
        {
            OpenedUrl = url;
            return Task.CompletedTask;
        }

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    // Stands in for a machine with no default browser/URL handler registered, so OpenUrlAsync
    // throws the way Process.Start / StartActivity does in that situation.
    private sealed class ThrowingDialogs : RecordingDialogs
    {
        public override Task OpenUrlAsync(string url) => throw new InvalidOperationException("kein Browser gefunden");
    }
}
