using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class AboutViewModelTests
{
    [Fact]
    public void Shows_the_app_name_and_descriptor()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Equal("Lagebuch", vm.AppName);
        Assert.Equal("Einsatzdokumentation", vm.Descriptor);
    }

    [Fact]
    public void Shows_the_passed_app_version()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Equal("0.1.0", vm.Version);
    }

    [Fact]
    public void Points_to_the_CodeForFire_lagebuch_repository()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Equal("https://github.com/CodeForFire/lagebuch", vm.RepositoryUrl);
    }

    [Fact]
    public void Names_the_MIT_license_and_the_copyright_holder()
    {
        // Must stay in sync with the LICENSE file in the repo root.
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Contains("MIT", vm.LicenseLine);
        Assert.Contains("Thomas Müller", vm.CopyrightLine);
        Assert.Contains("2026", vm.CopyrightLine);
    }

    [Fact]
    public void Close_raises_Closed_so_the_host_clears_the_overlay()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");
        var closed = false;
        vm.Closed += (_, _) => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public async Task OpenRepository_hands_the_repo_url_to_the_dialog_service()
    {
        var dialogs = new FakeDialogs();
        var vm = new AboutViewModel(dialogs, "0.1.0");

        await vm.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(vm.RepositoryUrl, dialogs.LastOpenedUrl);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failed_repository_open_is_reported_instead_of_crashing()
    {
        // A minimal OS install can lack any http handler — the dialog must survive it.
        var dialogs = new ThrowingUrlDialogs();
        var vm = new AboutViewModel(dialogs, "0.1.0");

        await vm.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("github.com/CodeForFire/lagebuch", vm.ErrorMessage);
    }

    private sealed class ThrowingUrlDialogs : IFileDialogService
    {
        public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);
        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);
        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);
        public Task OpenFileAsync(string path) => Task.CompletedTask;
        public Task OpenUrlAsync(string url) => throw new InvalidOperationException("Kein Handler registriert.");
        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }
}
