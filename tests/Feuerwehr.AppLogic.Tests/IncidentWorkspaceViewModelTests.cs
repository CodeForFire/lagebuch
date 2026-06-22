using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

internal sealed class FakeDialogs : IFileDialogService
{
    public string? ExportPath { get; set; }
    public Task<string?> PickSaveAsync(string suggestedFileName) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult(ExportPath);
}

public class IncidentWorkspaceViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => new(
        Roles: new[] { "EL" }, Status: Array.Empty<string>(), Equipment: Array.Empty<string>(),
        Districts: Array.Empty<string>(), RadioCallSigns: Array.Empty<string>(),
        Streets: Array.Empty<Street>(), ChecklistTemplate: Array.Empty<string>());

    private static IncidentWorkspaceViewModel NewWorkspace(out FakeStore store, out FixedClock clock, FakeDialogs? dialogs = null)
    {
        store = new FakeStore();
        clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", new[] { "A?" });
        return new IncidentWorkspaceViewModel(session, clock, Md(), dialogs ?? new FakeDialogs());
    }

    [Fact]
    public void Editing_a_child_autosaves()
    {
        var vm = NewWorkspace(out var store, out _);
        var before = store.SaveCount;
        vm.Etb.NewText = "Meldung";
        vm.Etb.NewDirection = EtbDirection.Internal;
        vm.Etb.AddEntryCommand.Execute(null);

        Assert.True(store.SaveCount > before);
        Assert.NotNull(vm.LastSavedAt);
    }

    [Fact]
    public void CloseIncident_makes_workspace_readonly_and_disables_edits()
    {
        var vm = NewWorkspace(out _, out _);
        Assert.True(vm.CloseIncidentCommand.CanExecute(null));

        vm.CloseIncidentCommand.Execute(null);

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.CloseIncidentCommand.CanExecute(null));
        Assert.True(vm.Etb.IsReadOnly);
        Assert.False(vm.Etb.AddEntryCommand.CanExecute(null));
        Assert.False(vm.Checklist.Items[0].ToggleCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportPdf_writes_file_when_path_chosen()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.pdf");
        var dialogs = new FakeDialogs { ExportPath = exportPath };
        var vm = NewWorkspace(out _, out _, dialogs);

        await vm.ExportPdfCommand.ExecuteAsync(null);

        Assert.True(File.Exists(exportPath));
        var bytes = await File.ReadAllBytesAsync(exportPath);
        Assert.Equal(0x25, bytes[0]); // %PDF
        File.Delete(exportPath);
    }

    [Fact]
    public async Task ExportPdf_does_nothing_when_cancelled()
    {
        var dialogs = new FakeDialogs { ExportPath = null };
        var vm = NewWorkspace(out _, out _, dialogs);
        await vm.ExportPdfCommand.ExecuteAsync(null); // should not throw
    }
}
