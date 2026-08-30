using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;
using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Tests;

public class LinksViewModelTests
{
    // Stands in for a machine with no default browser/URL handler registered, so OpenUrlAsync
    // throws the way Process.Start / StartActivity does in that situation.
    private sealed class ThrowingDialogs : IFileDialogService
    {
        public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);
        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);
        public Task OpenFileAsync(string path) => Task.CompletedTask;
        public Task OpenUrlAsync(string url) => throw new InvalidOperationException("kein Browser gefunden");
        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    [Fact]
    public async Task OpenAsync_passes_an_http_url_through_unchanged()
    {
        var dialogs = new FakeDialogs();
        var vm = new LinksViewModel(new[] { new Link("Wetterdienst", "https://dwd.de") }, dialogs);

        await vm.OpenCommand.ExecuteAsync(vm.Links[0]);

        Assert.Equal("https://dwd.de/", dialogs.LastOpenedUrl);
    }

    [Fact]
    public async Task OpenAsync_prepends_https_to_a_bare_domain()
    {
        var dialogs = new FakeDialogs();
        var vm = new LinksViewModel(new[] { new Link("Intranet", "intranet.feuerwehr.de") }, dialogs);

        await vm.OpenCommand.ExecuteAsync(vm.Links[0]);

        Assert.Equal("https://intranet.feuerwehr.de/", dialogs.LastOpenedUrl);
    }

    /// <summary>
    /// A Link's URL can come from an imported Stammdaten JSON file, not just what the user typed
    /// here -- a non-http(s) scheme (file://, javascript:, a bare local path treated as a URI) must
    /// never reach IFileDialogService.OpenUrlAsync, which on desktop shell-executes it.
    /// </summary>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.org")]
    [SuppressMessage("Design", "CA1054", Justification = "Test exercises links with free-form (even hostile) URL strings — that is the point of the test.")]
    public async Task OpenAsync_refuses_a_non_http_scheme(string url)
    {
        var dialogs = new FakeDialogs();
        var vm = new LinksViewModel(new[] { new Link("Böse", url) }, dialogs);

        await vm.OpenCommand.ExecuteAsync(vm.Links[0]);

        Assert.Null(dialogs.LastOpenedUrl);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task OpenAsync_surfaces_a_friendly_error_when_the_platform_launcher_throws()
    {
        var vm = new LinksViewModel(new[] { new Link("Wetterdienst", "https://dwd.de") }, new ThrowingDialogs());

        await vm.OpenCommand.ExecuteAsync(vm.Links[0]);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Wetterdienst", vm.ErrorMessage);
    }

    [Fact]
    public async Task OpenAsync_clears_a_previous_error_on_the_next_successful_open()
    {
        var dialogs = new FakeDialogs();
        var vm = new LinksViewModel(
            new[] { new Link("Böse", "javascript:alert(1)"), new Link("Wetterdienst", "https://dwd.de") }, dialogs);

        await vm.OpenCommand.ExecuteAsync(vm.Links[0]);
        Assert.NotNull(vm.ErrorMessage);

        await vm.OpenCommand.ExecuteAsync(vm.Links[1]);
        Assert.Null(vm.ErrorMessage);
    }
}
