using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class LinksViewModelTests
{
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
    public async Task OpenAsync_refuses_a_non_http_scheme(string url)
    {
        var dialogs = new FakeDialogs();
        var vm = new LinksViewModel(new[] { new Link("Böse", url) }, dialogs);

        await vm.OpenCommand.ExecuteAsync(vm.Links[0]);

        Assert.Null(dialogs.LastOpenedUrl);
    }
}
