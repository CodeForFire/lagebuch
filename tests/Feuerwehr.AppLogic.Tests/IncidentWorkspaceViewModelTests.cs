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
        Streets: Array.Empty<Street>(), ChecklistTemplate: Array.Empty<string>(), TruppTypes: Array.Empty<string>());

    private static IncidentWorkspaceViewModel NewWorkspace(out FakeStore store, out FixedClock clock, FakeDialogs? dialogs = null)
    {
        store = new FakeStore();
        clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", new[] { "A?" });
        return new IncidentWorkspaceViewModel(session, clock, new FakeTicker(), Md(), dialogs ?? new FakeDialogs(), new FakeAlarmService());
    }

    // A read-only-opened workspace over a still-open incident (upgradable via continue-editing).
    private static IncidentWorkspaceViewModel ReadOnlyWorkspace(out FixedClock clock, bool closed = false)
    {
        var store = new FakeStore();
        clock = new FixedClock(T0);
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", new[] { "A?" });
        if (closed)
            seed.Close(clock);
        var ro = IncidentSession.OpenReadOnly(store, "/x.fwincident");
        return new IncidentWorkspaceViewModel(ro, clock, new FakeTicker(), Md(), new FakeDialogs(), new FakeAlarmService());
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
    public void Setting_incident_number_autosaves_and_updates_domain()
    {
        var vm = NewWorkspace(out var store, out _);
        var before = store.SaveCount;

        vm.IncidentNumberInput = "B 4242";

        Assert.True(store.SaveCount > before);
        Assert.NotNull(vm.LastSavedAt);
        Assert.Equal("B 4242", store.Load("/x.fwincident").IncidentNumber!.Value);
    }

    [Fact]
    public void Clearing_incident_number_sets_value_to_null()
    {
        var vm = NewWorkspace(out var store, out _);
        vm.IncidentNumberInput = "B 4242";

        vm.IncidentNumberInput = "";

        Assert.Null(store.Load("/x.fwincident").IncidentNumber);
    }

    [Fact]
    public void Incident_number_input_seeded_from_existing_value()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", new[] { "A?" });
        seed.Incident.SetIncidentNumber(new Domain.ValueObjects.IncidentNumber("B 99"));
        seed.Save();
        var reopened = IncidentSession.Open(store, "/x.fwincident", new SessionOperator("Müller"));

        var vm = new IncidentWorkspaceViewModel(reopened, clock, new FakeTicker(), Md(), new FakeDialogs(), new FakeAlarmService());

        Assert.Equal("B 99", vm.IncidentNumberInput);
    }

    [Fact]
    public void CloseIncident_prompts_for_confirmation_before_closing()
    {
        var vm = NewWorkspace(out _, out _);
        Assert.True(vm.CloseIncidentCommand.CanExecute(null));

        vm.CloseIncidentCommand.Execute(null);

        // Nothing closed yet — a confirmation overlay is presented.
        Assert.NotNull(vm.PendingConfirm);
        Assert.False(vm.IsReadOnly);
    }

    [Fact]
    public void Confirming_close_makes_workspace_readonly_and_disables_edits()
    {
        var vm = NewWorkspace(out _, out _);
        vm.CloseIncidentCommand.Execute(null);

        vm.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.Null(vm.PendingConfirm);
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.CloseIncidentCommand.CanExecute(null));
        Assert.True(vm.Etb.IsReadOnly);
        Assert.False(vm.Etb.AddEntryCommand.CanExecute(null));
        Assert.True(vm.Checklist.IsReadOnly);
        Assert.True(vm.Checklist.Items[0].IsReadOnly);
    }

    // The closing entry is appended after the ETB grid is already populated, so it only
    // shows up because PerformClose rebuilds the children. Guards that rebuild.
    [Fact]
    public void Confirming_close_shows_the_closing_entry_in_the_etb_grid()
    {
        var vm = NewWorkspace(out _, out _);
        vm.CloseIncidentCommand.Execute(null);

        vm.PendingConfirm!.ConfirmCommand.Execute(null);

        // Newest-first grid: the closing entry sits above the opening one.
        Assert.Equal("Einsatz abgeschlossen", vm.Etb.Entries[0].Text);
        Assert.Equal("Einsatz begonnen", vm.Etb.Entries[1].Text);
    }

    [Fact]
    public void Continuing_editing_shows_the_resume_entry_in_the_etb_grid()
    {
        var vm = ReadOnlyWorkspace(out _);
        vm.ContinueEditingCommand.Execute(null);
        vm.PendingPrompt!.OperatorName = "Schmidt";
        vm.PendingPrompt.ConfirmCommand.Execute(null);

        vm.ConfirmContinueEditing();

        Assert.Equal("Bearbeitung fortgesetzt", vm.Etb.Entries[0].Text);
        Assert.Equal("Schmidt", vm.Etb.Entries[0].EnteredBy);
    }

    [Fact]
    public void Cancelling_close_leaves_incident_open()
    {
        var vm = NewWorkspace(out _, out _);
        vm.CloseIncidentCommand.Execute(null);

        vm.PendingConfirm!.CancelCommand.Execute(null);

        Assert.Null(vm.PendingConfirm);
        Assert.False(vm.IsReadOnly);
        Assert.True(vm.CloseIncidentCommand.CanExecute(null));
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

    [Fact]
    public void ContinueEditing_command_disabled_for_closed_incident()
    {
        var vm = ReadOnlyWorkspace(out _, closed: true);
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.CanContinueEditing);
        Assert.False(vm.ContinueEditingCommand.CanExecute(null));
    }

    [Fact]
    public void ContinueEditing_command_enabled_for_open_readonly_incident()
    {
        var vm = ReadOnlyWorkspace(out _);
        Assert.True(vm.IsReadOnly);
        Assert.True(vm.CanContinueEditing);
        Assert.True(vm.ContinueEditingCommand.CanExecute(null));
    }

    [Fact]
    public void Confirming_continue_editing_makes_workspace_editable_and_rebuilds_children()
    {
        var vm = ReadOnlyWorkspace(out _);

        vm.ContinueEditingCommand.Execute(null);
        Assert.NotNull(vm.PendingPrompt);
        vm.PendingPrompt!.OperatorName = "Schmidt";
        vm.PendingPrompt.ConfirmCommand.Execute(null);
        vm.ConfirmContinueEditing();

        Assert.Null(vm.PendingPrompt);
        Assert.False(vm.IsReadOnly);
        Assert.False(vm.CanContinueEditing);
        Assert.False(vm.Etb.IsReadOnly);
        vm.Etb.NewText = "Meldung";
        Assert.True(vm.Etb.AddEntryCommand.CanExecute(null));
    }

    [Fact]
    public void Cancelling_continue_editing_stays_readonly()
    {
        var vm = ReadOnlyWorkspace(out _);

        vm.ContinueEditingCommand.Execute(null);
        Assert.NotNull(vm.PendingPrompt);
        vm.CancelContinueEditing();

        Assert.Null(vm.PendingPrompt);
        Assert.True(vm.IsReadOnly);
        Assert.True(vm.CanContinueEditing);
    }

    [Fact]
    public void Continue_editing_then_add_etb_attributes_to_resuming_operator()
    {
        var vm = ReadOnlyWorkspace(out _);

        vm.ContinueEditingCommand.Execute(null);
        vm.PendingPrompt!.OperatorName = "Schmidt";
        vm.PendingPrompt.OperatorCallSign = "FFB 1";
        vm.PendingPrompt.ConfirmCommand.Execute(null);
        vm.ConfirmContinueEditing();

        vm.Etb.NewText = "Lagemeldung";
        vm.Etb.NewDirection = EtbDirection.Internal;
        vm.Etb.AddEntryCommand.Execute(null);

        Assert.Equal("Schmidt (FFB 1)", vm.Etb.Entries[0].EnteredBy);
    }

    [Fact]
    public void Editable_workspace_has_reminder()
    {
        var vm = NewWorkspace(out _, out _);

        Assert.NotNull(vm.Reminder);
    }

    [Fact]
    public void Closing_workspace_drops_reminder()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var session = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", new[] { "A?" });
        var ticker = new FakeTicker();
        var vm = new IncidentWorkspaceViewModel(session, clock, ticker, Md(), new FakeDialogs(), new FakeAlarmService());

        vm.CloseIncidentCommand.Execute(null);
        vm.PendingConfirm!.ConfirmCommand.Execute(null); // confirm the close

        Assert.Null(vm.Reminder);
        Assert.Equal(0, ticker.SubscriberCount); // disposed -> unsubscribed
    }

    [Fact]
    public void ReadOnly_opened_workspace_has_no_reminder()
    {
        var vm = ReadOnlyWorkspace(out _);

        Assert.Null(vm.Reminder);
    }
}
