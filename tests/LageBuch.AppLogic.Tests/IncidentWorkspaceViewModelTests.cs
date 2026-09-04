using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class IncidentWorkspaceViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

    private static IncidentWorkspaceViewModel EditableWorkspace(IIncidentHostController host)
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        return new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            host);
    }

    // --- Network sharing (no Tailscale required) --------------------------------------------
    [Fact]
    public async Task Toggling_sharing_on_starts_the_host_and_shows_its_hint()
    {
        var host = new FakeHostController(shareHint: "Erreichbar unter https://192.168.0.5:5859 · auf diesem Gerät: https://localhost:5859");
        var vm = EditableWorkspace(host);

        await vm.ToggleSharingCommand.ExecuteAsync(null);

        Assert.True(host.StartCalled);
        Assert.True(vm.IsSharing);
        Assert.Equal(host.ShareHint, vm.ShareStatus);
    }

    [Fact]
    public async Task A_failed_bind_surfaces_in_the_status_line_and_leaves_sharing_off()
    {
        var host = new FakeHostController(failWith: new InvalidOperationException("Port 5859 belegt"));
        var vm = EditableWorkspace(host);

        await vm.ToggleSharingCommand.ExecuteAsync(null);

        Assert.False(vm.IsSharing);
        Assert.Contains("Port 5859 belegt", vm.ShareStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sharing_hands_the_host_the_workspaces_own_master_data()
    {
        var host = new FakeHostController();
        var vm = EditableWorkspace(host);

        await vm.ToggleSharingCommand.ExecuteAsync(null);

        // The workspace's own set (Md()), not a re-read of the provider — so what joined clients
        // receive is what this device is actually operating with (#183).
        Assert.NotNull(host.LastMasterData);
        Assert.Equal(new[] { "EL" }, host.LastMasterData!.Roles);
    }

    private static IncidentWorkspaceViewModel NewWorkspace(out FakeStore store, out FixedClock clock, FakeDialogs? dialogs = null)
    {
        store = new FakeStore();
        clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        return new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            dialogs ?? new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
    }

    // A read-only-opened workspace over a still-open incident (upgradable via continue-editing).
    private static IncidentWorkspaceViewModel ReadOnlyWorkspace(out FixedClock clock, bool closed = false)
    {
        var store = new FakeStore();
        clock = new FixedClock(T0);
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        if (closed)
        {
            seed.Close();
        }

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        return new IncidentWorkspaceViewModel(
            ro,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
    }

    // --- ETB stays live ---------------------------------------------------------------------
    // Entries appended by a module used to sit invisible in the journal until the Einsatz was
    // closed, resumed or reopened, because Etb.Entries was a snapshot taken in BuildChildren and
    // only the ETB tab's own AddEntry inserted into it. Atemschutz has shipped with this.
    [Fact]
    public void An_entry_logged_from_the_kraefte_tab_appears_in_the_etb_immediately()
    {
        var vm = NewWorkspace(out _, out _);
        var before = vm.Etb.Entries.Count;

        vm.Forces.NewBrigade = "FFB Wache 1";
        vm.Forces.NewMannschaftCount = 9;
        vm.Forces.AddForceCommand.Execute(null);

        Assert.Equal(before + 1, vm.Etb.Entries.Count);

        // Newest first, matching how the tab renders.
        Assert.Contains("Einheit aufgenommen: FFB Wache 1", vm.Etb.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_logged_from_the_atemschutz_tab_appears_in_the_etb_immediately()
    {
        var vm = NewWorkspace(out _, out _);
        var before = vm.Etb.Entries.Count;

        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = "Müller";
        vm.Scba.NewTruppmann = "Schmidt";
        vm.Scba.AddTruppCommand.Execute(null);

        Assert.Equal(before + 1, vm.Etb.Entries.Count);
        Assert.Contains("Angriffstrupp", vm.Etb.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Completing_a_mandatory_checklist_item_appears_in_the_etb_immediately()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("Blaulicht aus?", true) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
        var before = vm.Etb.Entries.Count;

        vm.ChecklistAufbau.Items[0].IsDone = true;

        Assert.Equal(before + 1, vm.Etb.Entries.Count);
        Assert.Equal("Checkliste Aufbau abgeschlossen: alle Pflichtpunkte erledigt", vm.Etb.Entries[0].Text);
    }

    [Fact]
    public void Repeated_saves_do_not_duplicate_etb_rows()
    {
        // Sync runs on every OnChanged, so it has to be idempotent -- a Bemerkung edit alone
        // triggers it without adding a journal entry.
        var vm = NewWorkspace(out _, out _);
        vm.Forces.NewBrigade = "FFB Wache 1";
        vm.Forces.NewMannschaftCount = 9;
        vm.Forces.AddForceCommand.Execute(null);
        var after = vm.Etb.Entries.Count;

        vm.Forces.Forces[0].Notes = "über DLK angefordert";
        vm.Forces.Forces[0].Notes = "über DLK angefordert, Wasser ab";

        Assert.Equal(after, vm.Etb.Entries.Count);
    }

    [Fact]
    public void Typing_in_the_etb_tab_still_lists_the_entry_once()
    {
        var vm = NewWorkspace(out _, out _);
        var before = vm.Etb.Entries.Count;

        vm.Etb.NewText = "Lagemeldung erhalten";
        vm.Etb.AddEntryCommand.Execute(null);

        Assert.Equal(before + 1, vm.Etb.Entries.Count);
        Assert.Equal("Lagemeldung erhalten", vm.Etb.Entries[0].Text);
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
    public void Setting_IncidentNumberInput_directly_does_not_write_back()
    {
        // IncidentNumberInput is a display projection; the only write-back path is the
        // ConfirmIncidentNumberCommand flow below (#69) -- assigning the bound property directly
        // (as a plain data-bound control would) must not autosave or mutate the domain.
        var vm = NewWorkspace(out var store, out _);
        var before = store.SaveCount;

        vm.IncidentNumberInput = "B 4242";

        Assert.Equal(before, store.SaveCount);
        Assert.Null(store.Load("/x.fwincident").IncidentNumber);
    }

    [Fact]
    public void Incident_number_input_seeded_from_existing_value()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        seed.Incident.SetIncidentNumber(new Domain.ValueObjects.IncidentNumber("B 99"));
        seed.Save();
        var reopened = LocalIncidentSession.Open(store, clock, "/x.fwincident", new SessionOperator("Müller"));

        var vm = new IncidentWorkspaceViewModel(
            reopened,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        Assert.Equal("B 99", vm.IncidentNumberInput);
    }

    // --- Header hero + Einsatznummer add-later (#69) -----------------------------------------
    [Fact]
    public void HeroText_shows_the_keyword_when_set()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>(),
            keyword: "B3P");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        Assert.Equal("B3P", vm.HeroText);
        Assert.True(vm.ShowEinsatznummerSlot);
        Assert.False(vm.HasEinsatznummer);
        Assert.True(vm.ShowAddEinsatznummerAffordance);
        Assert.False(vm.ShowEinsatznummerChip);
    }

    [Fact]
    public void HeroText_falls_back_to_the_einsatznummer_when_there_is_no_keyword()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>(),
            incidentNumber: new Domain.ValueObjects.IncidentNumber("B 1.2 260715 123"));
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        Assert.Equal("B 1.2 260715 123", vm.HeroText);

        // The number is already the hero -- no redundant chip alongside it.
        Assert.False(vm.ShowEinsatznummerSlot);
    }

    [Fact]
    public void HeroText_falls_back_to_a_placeholder_when_neither_is_set()
    {
        var vm = NewWorkspace(out _, out _);

        Assert.Equal("Unbenannter Einsatz", vm.HeroText);
        Assert.False(vm.ShowEinsatznummerSlot);
    }

    [Fact]
    public void Begin_confirm_flow_sets_the_einsatznummer_persists_it_and_shows_the_chip()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>(),
            keyword: "B3P");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        vm.BeginEditIncidentNumberCommand.Execute(null);
        Assert.True(vm.IsEditingIncidentNumber);
        Assert.True(vm.ShowEinsatznummerEdit);
        Assert.False(vm.ShowAddEinsatznummerAffordance);

        vm.IncidentNumberEditInput = "B 1.2 260715 123";
        vm.ConfirmIncidentNumberCommand.Execute(null);

        Assert.False(vm.IsEditingIncidentNumber);
        Assert.Equal("B 1.2 260715 123", vm.IncidentNumberInput);
        Assert.True(vm.HasEinsatznummer);
        Assert.True(vm.ShowEinsatznummerChip);
        Assert.Equal("B 1.2 260715 123", store.Load("/x.fwincident").IncidentNumber!.Value);
    }

    [Fact]
    public void Cancel_edit_discards_without_mutating()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>(),
            keyword: "B3P");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        vm.BeginEditIncidentNumberCommand.Execute(null);
        vm.IncidentNumberEditInput = "B 1.2 260715 123";
        vm.CancelEditIncidentNumberCommand.Execute(null);

        Assert.False(vm.IsEditingIncidentNumber);
        Assert.False(vm.HasEinsatznummer);
        Assert.Null(store.Load("/x.fwincident").IncidentNumber);
    }

    [Fact]
    public void Confirm_is_disabled_until_the_edit_input_is_non_blank()
    {
        var vm = NewWorkspace(out _, out _);
        vm.BeginEditIncidentNumberCommand.Execute(null);
        Assert.False(vm.ConfirmIncidentNumberCommand.CanExecute(null));

        vm.IncidentNumberEditInput = "   ";
        Assert.False(vm.ConfirmIncidentNumberCommand.CanExecute(null));

        vm.IncidentNumberEditInput = "B 1";
        Assert.True(vm.ConfirmIncidentNumberCommand.CanExecute(null));
    }

    [Fact]
    public void Editing_the_einsatznummer_is_blocked_on_a_readonly_workspace()
    {
        var vm = ReadOnlyWorkspace(out _, closed: true);

        Assert.False(vm.CanEditIncidentNumber);
        Assert.False(vm.BeginEditIncidentNumberCommand.CanExecute(null));
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
        Assert.True(vm.ChecklistAufbau.IsReadOnly);
        Assert.True(vm.ChecklistAufbau.Items[0].IsReadOnly);
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
    public void ContinueEditing_prompt_offers_the_master_data_call_signs()
    {
        var callSigns = new[] { "FFB 1/40/1", "Aich 42/1" };
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new IncidentWorkspaceViewModel(
            ro,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty with { RadioCallSigns = callSigns },
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        vm.ContinueEditingCommand.Execute(null);

        Assert.Equal(callSigns, vm.PendingPrompt!.CallSignOptions);
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
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        var ticker = new FakeTicker();
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

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

    // BuildChildren replaces 11 child view models on every lifecycle rebuild (ctor, continue-editing,
    // close, remote read-only flip); each child subscribes to _session.Changed in its own constructor.
    // Before this fix, 7 of them were simply overwritten instead of disposed first, so their old
    // instances stayed subscribed forever — a leak that grows with every rebuild.
    [Fact]
    public void Rebuilding_children_does_not_leak_Changed_subscriptions()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        var session = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            Md(),
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        vm.ContinueEditingCommand.Execute(null); // rebuild #2
        vm.PendingPrompt!.OperatorName = "Schmidt";
        vm.PendingPrompt.ConfirmCommand.Execute(null);
        vm.ConfirmContinueEditing();
        var afterFirstRebuild = ChangedSubscriberCount(session);

        vm.CloseIncidentCommand.Execute(null); // rebuild #3
        vm.PendingConfirm!.ConfirmCommand.Execute(null);
        var afterSecondRebuild = ChangedSubscriberCount(session);

        Assert.Equal(afterFirstRebuild, afterSecondRebuild);
    }

    private static int ChangedSubscriberCount(LocalIncidentSession session)
    {
        var field = typeof(LocalIncidentSession).GetField(
            nameof(LocalIncidentSession.Changed), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var handler = (Delegate?)field!.GetValue(session);
        return handler?.GetInvocationList().Length ?? 0;
    }

    [Fact]
    public void Etb_add_and_create_task_opens_prefilled_task_dialog()
    {
        var vm = NewWorkspace(out _, out _);
        vm.Etb.NewText = "Meldung an ILS";
        vm.Etb.AddEntryAndCreateTaskCommand.Execute(null);

        Assert.NotNull(vm.PendingTaskDialog);
        Assert.Equal("Meldung an ILS", vm.PendingTaskDialog!.Text);
        Assert.Contains(vm.Etb.Entries, e => e.Text == "Meldung an ILS");
    }
}

internal sealed class FakeDialogs : IFileDialogService
{
    public string? ExportPath { get; set; }

    public string? AttachmentPath { get; set; }

    public string? LastOpenedPath { get; private set; }

    public string? LastOpenedUrl { get; private set; }

    public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>("/x.fwincident");

    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult(ExportPath);

    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

    public Task<string?> PickAttachmentAsync() => Task.FromResult(AttachmentPath);

    public Task OpenFileAsync(string path)
    {
        LastOpenedPath = path;
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(string url)
    {
        LastOpenedUrl = url;
        return Task.CompletedTask;
    }

    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}

// Hostable controller double: unlike NoopIncidentHostController (CanHost=false, which hides the
// toggle) this reports CanHost=true so ToggleSharing runs. Optionally fails StartAsync to exercise
// the bind-failure path (e.g. port already in use).
internal sealed class FakeHostController : IIncidentHostController
{
    private readonly Exception? _failWith;

    public FakeHostController(string shareHint = "Erreichbar unter https://192.168.0.5:5859", Exception? failWith = null)
    {
        ShareHint = shareHint;
        _failWith = failWith;
    }

    public bool CanHost => true;

    public bool IsHosting { get; private set; }

    public string? ShareHint { get; }

    public string? SharePin { get; private set; }

    public bool StartCalled { get; private set; }

    /// <summary>The Stammdaten the workspace handed over when sharing started (#183).</summary>
    public MasterDataSet? LastMasterData { get; private set; }

    public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default)
    {
        StartCalled = true;
        LastMasterData = masterData;
        if (_failWith is not null)
        {
            throw _failWith;
        }

        IsHosting = true;
        SharePin = "1234";
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        IsHosting = false;
        SharePin = null;
        return Task.CompletedTask;
    }
}
