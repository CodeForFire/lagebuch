using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

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
        Assert.Contains("Wetterdienst", vm.ErrorMessage, StringComparison.Ordinal);
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

    // Issue #262 (UX review, "Links" section): the tab was a flat list with no way to narrow it,
    // which stops scaling as soon as a Wehr keeps more than a handful of Stammdaten-Links.
    private static LinksViewModel WithThreeLinks() => new(
        new[]
        {
            new Link("Wetterdienst", "https://dwd.de"),
            new Link("Kartendienst", "https://example.org/karte"),
            new Link("Gefahrgut", "https://example.org/hazmat"),
        },
        new FakeDialogs());

    [Fact]
    public void Every_link_is_visible_before_anything_is_typed()
    {
        var vm = WithThreeLinks();

        Assert.Equal(3, vm.VisibleLinks.Count);
        Assert.False(vm.IsFiltered);
    }

    [Fact]
    public void Filtering_by_name_ignores_case()
    {
        var vm = WithThreeLinks();

        vm.FilterText = "wetter";

        Assert.Equal("Wetterdienst", Assert.Single(vm.VisibleLinks).Name);
    }

    [Fact]
    public void Filtering_matches_the_url_as_well_as_the_name()
    {
        var vm = WithThreeLinks();

        vm.FilterText = "hazmat";

        Assert.Equal("Gefahrgut", Assert.Single(vm.VisibleLinks).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_filter_shows_every_link(string filter)
    {
        var vm = WithThreeLinks();
        vm.FilterText = "wetter";

        vm.FilterText = filter;

        Assert.Equal(3, vm.VisibleLinks.Count);
    }

    /// <summary>
    /// The two empty states must stay distinguishable: "no Links configured at all" (Stammdaten
    /// are empty) reads differently to the operator than "nothing matched what you typed", so
    /// filtering must never touch <see cref="LinksViewModel.Links"/> itself.
    /// </summary>
    [Fact]
    public void A_filter_that_matches_nothing_empties_the_visible_list_but_not_the_full_one()
    {
        var vm = WithThreeLinks();

        vm.FilterText = "Drehleiter";

        Assert.Empty(vm.VisibleLinks);
        Assert.Equal(3, vm.Links.Count);
    }

    [Fact]
    public void Clearing_the_filter_restores_every_link()
    {
        var vm = WithThreeLinks();
        vm.FilterText = "wetter";

        vm.ClearFilterCommand.Execute(null);

        Assert.Equal(3, vm.VisibleLinks.Count);
        Assert.Equal(string.Empty, vm.FilterText);
    }

    // Drives the clear button's visibility, so it has to follow FilterText rather than be set by
    // ApplyFilter -- a filter that matches everything is still an active filter.
    [Fact]
    public void IsFiltered_tracks_whether_a_search_term_is_present()
    {
        var vm = WithThreeLinks();

        vm.FilterText = "e";
        Assert.True(vm.IsFiltered);
        Assert.Equal(3, vm.VisibleLinks.Count);

        vm.FilterText = "  ";
        Assert.False(vm.IsFiltered);
    }

    [Fact]
    public async Task Opening_a_link_still_works_while_a_filter_is_active()
    {
        var dialogs = new FakeDialogs();
        var vm = new LinksViewModel(
            new[] { new Link("Wetterdienst", "https://dwd.de"), new Link("Kartendienst", "https://example.org/karte") },
            dialogs);

        vm.FilterText = "karten";
        await vm.OpenCommand.ExecuteAsync(vm.VisibleLinks[0]);

        Assert.Equal("https://example.org/karte", dialogs.LastOpenedUrl);
    }
}
