using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Shared.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Acceptance.Tests;

internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _d = new();
    public void Save(string path, Incident incident) => _d[path] = incident;
    public Incident Load(string path) => _d[path];
    public IncidentState? TryReadState(string path) => _d.TryGetValue(path, out var i) ? i.State : null;
    private readonly Dictionary<string, byte[]> _files = new();
    public void SaveFileBytes(string path, string storageFileName, byte[] bytes) => _files[$"{path}/{storageFileName}"] = bytes;
    public byte[]? TryReadFileBytes(string path, string storageFileName) => _files.TryGetValue($"{path}/{storageFileName}", out var b) ? b : null;
}
internal sealed class FakeDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);
    public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);
    public Task OpenFileAsync(string path) => Task.CompletedTask;
    public Task OpenUrlAsync(string url) => Task.CompletedTask;
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}
// Mirrors FakeDialogs but returns a caller-supplied path from PickAttachmentAsync, so a UI-driven
// "add file" test can exercise a real AddFileCommand execution without a real OS file picker.
internal sealed class AttachmentDialogs : IFileDialogService
{
    private readonly string? _path;
    public AttachmentDialogs(string? path) => _path = path;
    public string? LastOpenedPath { get; private set; }
    public string? LastOpenedUrl { get; private set; }
    public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);
    public Task<string?> PickAttachmentAsync() => Task.FromResult(_path);
    public Task OpenFileAsync(string path) { LastOpenedPath = path; return Task.CompletedTask; }
    public Task OpenUrlAsync(string url) { LastOpenedUrl = url; return Task.CompletedTask; }
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}
internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
}
internal sealed class NoopTicker : ITicker
{
    public IDisposable Subscribe(Action onTick) => new Sub();
    private sealed class Sub : IDisposable { public void Dispose() { } }
}
internal sealed class NoopAlarmService : IAlarmService
{
    public void Start() { }
    public void Stop() { }
    public void Play(AlarmSound sound) { }
}

public class WorkspaceAcceptanceTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("Blaulicht aus?", false) },
        TruppTypes = new[] { "Angriffstrupp" },
        Brigades = new[] { "FFB Wache 1", "Aich" },
        UnitStatus = new[] { "Alarmiert", "Im Einsatz" },
        Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
    };

    private static IncidentWorkspaceViewModel BuildWorkspace(out LocalIncidentSession session)
    {
        session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            new[] { ("Blaulicht aus?", false) }, Array.Empty<(string, bool)>());
        return new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
    }

    // A read-only-opened workspace over a still-open (or optionally closed) incident.
    private static IncidentWorkspaceViewModel BuildReadOnlyWorkspace(bool closed = false)
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident", new[] { ("Blaulicht aus?", false) }, Array.Empty<(string, bool)>());
        if (closed)
            seed.Close();
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        return new IncidentWorkspaceViewModel(ro, clock, new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
    }

    [AvaloniaFact]
    public void Workspace_renders_with_eight_tabs()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(8, tabs.Items.Count);
    }

    [AvaloniaFact]
    public void Adding_etb_entry_via_ui_updates_the_grid()
    {
        var vm = BuildWorkspace(out var session);
        var view = new EtbView { DataContext = vm.Etb };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        var textBox = view.GetControl<TextBox>("EtbTextBox");
        var addButton = view.GetControl<Button>("EtbAddButton");

        textBox.Focus();
        window.KeyTextInput("Lagemeldung erhalten");
        // Ensure the binding pushed the value:
        Assert.Equal("Lagemeldung erhalten", vm.Etb.NewText);

        addButton.Command!.Execute(null);

        // The grid already holds the automatic "Einsatz begonnen" entry; it is newest-first,
        // so the entry just typed appears above it.
        Assert.Equal(2, session.Incident.Journal.Count);
        Assert.Equal("Lagemeldung erhalten", vm.Etb.Entries[0].Text);
        Assert.Equal("Einsatz begonnen", vm.Etb.Entries[1].Text);
    }

    // #73: editing an existing manual ETB entry shows a "bearbeitet" tag on the row and the edit
    // panel below the grid. Doubles as the PR before/after screenshot capture.
    [AvaloniaFact]
    public void Editing_an_etb_entry_updates_the_row_and_shows_the_bearbeitet_tag()
    {
        var vm = BuildWorkspace(out var session);
        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung erhalten", from: "Leitstelle");
        var view = new EtbView { DataContext = vm.Etb };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using (var before = window.CaptureRenderedFrame()!)
            before.Save(Path.Combine(dir, "etb-edit-before.png"));

        var row = Assert.Single(vm.Etb.Entries, e => e.Text == "Lagemeldung erhalten");
        Assert.False(row.WasEdited);
        row.BeginEditCommand.Execute(null);
        Assert.True(vm.Etb.IsEditing);

        using (var editing = window.CaptureRenderedFrame()!)
            editing.Save(Path.Combine(dir, "etb-edit-panel.png"));

        var editTextBox = view.GetControl<TextBox>("EditTextBox");
        editTextBox.Focus();
        // Replace the whole content: select-all then type, mirroring how a user would correct it.
        editTextBox.SelectAll();
        window.KeyTextInput("Lagemeldung korrigiert");
        Assert.Equal("Lagemeldung korrigiert", vm.Etb.EditText);

        var saveButton = view.GetControl<Button>("SaveEditButton");
        saveButton.Command!.Execute(null);

        Assert.False(vm.Etb.IsEditing);
        var edited = Assert.Single(vm.Etb.Entries, e => e.Text == "Lagemeldung korrigiert");
        Assert.True(edited.WasEdited);
        Assert.Equal("Lagemeldung erhalten", Assert.Single(edited.Edits).PreviousText);

        using (var after = window.CaptureRenderedFrame()!)
            after.Save(Path.Combine(dir, "etb-edit-after.png"));

        // History viewing is decoupled from editing (security review, #73) -- capture it separately.
        edited.ShowHistoryCommand.Execute(null);
        Assert.NotNull(vm.Etb.HistoryEntry);
        using (var history = window.CaptureRenderedFrame()!)
            history.Save(Path.Combine(dir, "etb-edit-history.png"));
    }

    // System-generated entries (the automatic "Einsatz begonnen" line) are never editable —
    // BeginEditCommand must refuse them regardless of what the UI shows.
    [AvaloniaFact]
    public void System_entries_have_no_working_edit_affordance()
    {
        var vm = BuildWorkspace(out _);

        var systemRow = Assert.Single(vm.Etb.Entries, e => e.Text == "Einsatz begonnen");

        Assert.False(systemRow.IsEditable);
        Assert.False(systemRow.BeginEditCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Files_tab_renders_an_already_attached_file()
    {
        var vm = BuildWorkspace(out var session);
        session.Incident.AddFile(new FixedClock(), session.Operator!, "brand.jpg", "image/jpeg", 2048);
        var view = new FilesView { DataContext = new FilesViewModel(session, new FakeDialogs(), () => { }) };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        // The name is an editable TextBox now (display name), not a TextBlock.
        var names = view.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).ToList();
        Assert.Contains("brand.jpg", names);
    }

    [AvaloniaFact]
    public void Renaming_a_file_via_ui_writes_through_to_the_domain()
    {
        var vm = BuildWorkspace(out var session);
        session.Incident.AddFile(new FixedClock(), session.Operator!, "brand.jpg", "image/jpeg", 2048);
        var filesVm = new FilesViewModel(session, new FakeDialogs(), () => { });
        var view = new FilesView { DataContext = filesVm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        var nameBox = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "brand.jpg");
        nameBox.Focus();
        nameBox.SelectAll();
        window.KeyTextInput("Küchenbrand");

        Assert.Equal("Küchenbrand", filesVm.Files[0].DisplayName);
        Assert.Equal("Küchenbrand", session.Incident.Files.Single().DisplayName);
        Assert.Equal("brand.jpg", session.Incident.Files.Single().FileName); // the original name is untouched
    }

    [AvaloniaFact]
    public void Adding_file_via_ui_updates_the_list_and_logs_to_the_etb()
    {
        var vm = BuildWorkspace(out var session);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"brand-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try
        {
            var dialogs = new AttachmentDialogs(path);
            var filesVm = new FilesViewModel(session, dialogs, () => { });
            var view = new FilesView { DataContext = filesVm };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();

            var addButton = view.GetControl<Button>("AddFileButton");
            addButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs(); // let the async command's continuation reach the UI thread

            Assert.Single(session.Incident.Files);
            Assert.Contains(session.Incident.Journal, e => e.Text.StartsWith("Datei hinzugefügt:"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void Clicking_checklist_checkbox_persists_done_state()
    {
        var vm = BuildWorkspace(out var session);
        var view = new ChecklistView { DataContext = vm.ChecklistAufbau };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();

        var checkBox = view.GetVisualDescendants().OfType<CheckBox>().First();

        // Simulate a real user click on the checkbox (drive the actual input pipeline,
        // not the command directly, so the IsChecked two-way binding participates too).
        var center = checkBox.TranslatePoint(
            new Avalonia.Point(checkBox.Bounds.Width / 2, checkBox.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);

        Assert.True(checkBox.IsChecked);
        Assert.True(vm.ChecklistAufbau.Items[0].IsDone);
        Assert.True(session.Incident.ChecklistAufbau[0].IsDone);
    }

    // The AUFBAU/ABBAU tab headers carry a status dot: red while a mandatory item is still open,
    // green once every mandatory item in that list is checked (#72).
    [AvaloniaFact]
    public void Aufbau_tab_dot_turns_green_once_its_mandatory_item_is_checked()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            new[] { ("Blaulicht aus?", true) }, Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(), Md(),
            new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();

        var completeDot = view.GetControl<Avalonia.Controls.Shapes.Ellipse>("AufbauCompleteDot");
        var incompleteDot = view.GetControl<Avalonia.Controls.Shapes.Ellipse>("AufbauIncompleteDot");
        Assert.False(completeDot.IsVisible);
        Assert.True(incompleteDot.IsVisible);
        // The neighboring ABBAU list has no items -- vacuously complete from the start.
        Assert.True(view.GetControl<Avalonia.Controls.Shapes.Ellipse>("AbbauCompleteDot").IsVisible);

        // Capture the PR before/after screenshots (real Skia backend rasterizes embedded fonts).
        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using (var before = window.CaptureRenderedFrame()!)
            before.Save(Path.Combine(dir, "checkliste-aufbau-abbau-before.png"));

        vm.ChecklistAufbau.Items[0].IsDone = true;

        Assert.True(completeDot.IsVisible);
        Assert.False(incompleteDot.IsVisible);

        using (var after = window.CaptureRenderedFrame()!)
            after.Save(Path.Combine(dir, "checkliste-aufbau-abbau-after.png"));
    }

    [AvaloniaFact]
    public void Closing_incident_shows_readonly_banner_and_disables_add()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        vm.CloseIncidentCommand.Execute(null);
        vm.PendingConfirm!.ConfirmCommand.Execute(null); // confirm the close prompt

        var banner = window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "ReadOnlyBanner");
        Assert.True(banner.IsVisible);
        Assert.True(vm.Etb.IsReadOnly);
        Assert.False(vm.Etb.AddEntryCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Open_readonly_open_incident_shows_continue_editing_action()
    {
        var vm = BuildReadOnlyWorkspace();
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "ContinueEditingButton");
        Assert.True(button.IsVisible);
        Assert.True(vm.Etb.IsReadOnly);
    }

    [AvaloniaFact]
    public void Continue_editing_prompts_for_operator_and_enables_editing()
    {
        var vm = BuildReadOnlyWorkspace();
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        // Open the prompt via the UI button.
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "ContinueEditingButton");
        button.Command!.Execute(null);
        Assert.NotNull(vm.PendingPrompt);

        // Fill and confirm the prompt; the view's code-behind auto-applies the result.
        vm.PendingPrompt!.OperatorName = "Schmidt";
        vm.PendingPrompt.ConfirmCommand.Execute(null);

        Assert.Null(vm.PendingPrompt);
        Assert.False(vm.IsReadOnly);
        Assert.False(vm.Etb.IsReadOnly);

        var banner = window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "ReadOnlyBanner");
        Assert.False(banner.IsVisible);
    }

    [AvaloniaFact]
    public void Closed_incident_has_no_continue_editing_action()
    {
        var vm = BuildReadOnlyWorkspace(closed: true);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "ContinueEditingButton");
        Assert.False(button.IsVisible);
        Assert.False(vm.CanContinueEditing);
    }

    [AvaloniaFact]
    public void Editable_workspace_shows_reminder_bar()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var reminderBar = window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "ReminderBar");
        Assert.True(reminderBar.IsVisible);
        Assert.True(vm.HasReminder);

        // The reminder is autonomous — it is already running (no manual start button).
        Assert.True(vm.Reminder!.IsRunning);
        var countdown = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Name == "ReminderCountdownText");
        Assert.True(countdown.IsVisible);
    }

    [AvaloniaFact]
    public void Readonly_workspace_does_not_show_reminder_bar()
    {
        var vm = BuildReadOnlyWorkspace();
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var reminderBar = window.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Name == "ReminderBar");
        Assert.NotNull(reminderBar);
        Assert.False(reminderBar.IsVisible);
        Assert.False(vm.HasReminder);
    }

    [AvaloniaFact]
    public void Scba_control_reminder_appears_in_global_header_when_a_trupp_is_under_air()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ScbaControlBar");
        Assert.False(bar.IsVisible); // nothing under air yet

        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = "Müller";
        vm.Scba.NewTruppmann = "Schmidt";
        vm.Scba.AddTruppCommand.Execute(null);
        var row = vm.Scba.Trupps[^1];
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        Assert.True(vm.Scba.HasControlReminder);
        Assert.True(bar.IsVisible);
    }

    // Binding the bare EtbDirection enum made Avalonia fall back to Enum.ToString(), so the
    // picker read "Incoming" while the grid beside it read "Eingang". A ViewModel-level
    // assertion cannot catch that regression — it only shows up once the control renders.
    [AvaloniaFact]
    public void Etb_direction_picker_renders_german_labels()
    {
        var vm = BuildWorkspace(out _);
        var view = new EtbView { DataContext = vm.Etb };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = view.GetControl<ComboBox>("DirectionBox");

        // Closed: the selection box shows the label, not the enum identifier.
        Assert.Equal("Eingang", Text(box).Single());

        box.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        // Open: the list itself is realized into an OverlayPopupHost, which is a sibling of the
        // ComboBox rather than a descendant — so the options have to be read through the popup.
        var popupHost = Assert.IsAssignableFrom<Control>(
            box.GetVisualDescendants().OfType<Popup>().Single().Host);
        var options = Text(popupHost);

        Assert.Equal(new[] { "Eingang", "Ausgang", "Intern" }, options);

        static string[] Text(Control c) => c.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray()!;
    }

    // The Funktionen tab had no UI-level coverage at all before issue #17 widened it.
    [AvaloniaFact]
    public void Assigning_a_function_via_the_ui_fills_the_grid_and_stamps_von()
    {
        var vm = BuildWorkspace(out var session);
        var view = new RolesView { DataContext = vm.Roles };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Typing a roster name must pull the number across without any extra keystrokes.
        view.GetControl<AutoCompleteBox>("PersonNameBox").Text = "Mustermann, Max";
        Assert.Equal("01 71 / 1 23 45 67", vm.Roles.NewPhone);

        vm.Roles.NewRole = "EL";
        view.GetControl<TextBox>("SectionBox").Text = "Abschnitt Nord";
        Dispatcher.UIThread.RunJobs();
        vm.Roles.AddRoleCommand.Execute(null);

        var assignment = Assert.Single(session.Incident.Roles);
        Assert.Equal("Abschnitt Nord", assignment.Section);
        Assert.Equal("01 71 / 1 23 45 67", assignment.Phone);
        Assert.NotNull(assignment.From);

        var row = Assert.Single(vm.Roles.Roles);
        Assert.True(row.IsRunning);

        // Handynummer is a live cell bound two-way to the row, mirroring ForcesView's Bemerkung
        // column: setting it is exactly the edit the grid's TextBox performs on a keystroke.
        row.Phone = "01 71 / 9 99 99 99";
        Assert.Equal("01 71 / 9 99 99 99", Assert.Single(session.Incident.Roles).Phone);
    }

    // The Funktionen tab had no transfer/filter coverage at all before issue #75 replaced the
    // standalone "beenden" button with a handover flow and a nur-aktuell filter.
    [AvaloniaFact]
    public void Transferring_a_role_via_the_ui_ends_the_old_row_and_hides_it_behind_the_filter()
    {
        var vm = BuildWorkspace(out var session);
        // No explicit `from`: the session's own FixedClock is what TransferRole stamps the handover
        // with, and this test doesn't have a handle on it -- an unstamped Von avoids a spurious
        // "Bis vor Von" the wall clock would otherwise trigger.
        session.AssignRole("EL", "Müller", callSign: "FFB 12/1", section: "Abschnitt Nord");
        var view = new RolesView { DataContext = vm.Roles };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using (var before = window.CaptureRenderedFrame()!)
            before.Save(Path.Combine(dir, "roles-transfer-before.png"));

        var row = Assert.Single(vm.Roles.Roles);
        Assert.True(row.BeginTransferCommand.CanExecute(null));
        row.BeginTransferCommand.Execute(null);
        Assert.True(vm.Roles.IsTransferring);

        using (var panel = window.CaptureRenderedFrame()!)
            panel.Save(Path.Combine(dir, "roles-transfer-panel.png"));

        view.GetControl<AutoCompleteBox>("TransferPersonNameBox").Text = "Schmidt";
        Assert.Equal("Schmidt", vm.Roles.TransferPersonName);

        var confirmButton = view.GetControl<Button>("ConfirmTransferButton");
        confirmButton.Command!.Execute(null);

        Assert.False(vm.Roles.IsTransferring);
        Assert.Equal(2, session.Incident.Roles.Count);
        // "Nur aktuell" is the default filter: the handed-over assignment drops out of the grid.
        var current = Assert.Single(vm.Roles.Roles);
        Assert.Equal("Schmidt", current.PersonName);
        Assert.True(current.IsRunning);

        vm.Roles.ShowAllRoles = true;
        Assert.Equal(2, vm.Roles.Roles.Count);
        Assert.Contains(vm.Roles.Roles, r => r.PersonName == "Müller" && !r.IsRunning);

        using (var after = window.CaptureRenderedFrame()!)
            after.Save(Path.Combine(dir, "roles-transfer-after.png"));
    }
}
