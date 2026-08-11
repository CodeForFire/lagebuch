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
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Acceptance.Tests;

internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _d = new();
    public void Save(string path, Incident incident) => _d[path] = incident;
    public Incident Load(string path) => _d[path];
    public IncidentState? TryReadState(string path) => _d.TryGetValue(path, out var i) ? i.State : null;
}
internal sealed class FakeDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string s) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);
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
}

public class WorkspaceAcceptanceTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplate = new[] { "Blaulicht aus?" },
        TruppTypes = new[] { "Angriffstrupp" },
        Brigades = new[] { "FFB Wache 1", "Aich" },
        UnitStatus = new[] { "Alarmiert", "Im Einsatz" },
        Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
    };

    private static IncidentWorkspaceViewModel BuildWorkspace(out IncidentSession session)
    {
        session = IncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", new[] { "Blaulicht aus?" });
        return new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService());
    }

    // A read-only-opened workspace over a still-open (or optionally closed) incident.
    private static IncidentWorkspaceViewModel BuildReadOnlyWorkspace(bool closed = false)
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident", new[] { "Blaulicht aus?" });
        if (closed)
            seed.Close(clock);
        var ro = IncidentSession.OpenReadOnly(store, "/x.fwincident");
        return new IncidentWorkspaceViewModel(ro, clock, new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService());
    }

    [AvaloniaFact]
    public void Workspace_renders_with_five_tabs()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(5, tabs.Items.Count);
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

    [AvaloniaFact]
    public void Clicking_checklist_checkbox_persists_done_state()
    {
        var vm = BuildWorkspace(out var session);
        var view = new ChecklistView { DataContext = vm.Checklist };
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
        Assert.True(vm.Checklist.Items[0].IsDone);
        Assert.True(session.Incident.Checklist[0].IsDone);
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

        var startButton = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "ReminderStartButton");
        Assert.True(startButton.IsVisible);
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
        row.EndCommand.Execute(null);
        Assert.False(row.IsRunning);
        Assert.NotNull(Assert.Single(session.Incident.Roles).To);
    }
}
