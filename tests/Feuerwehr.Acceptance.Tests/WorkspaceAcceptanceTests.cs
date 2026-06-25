using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Feuerwehr.App.Views;
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
}
internal sealed class FakeDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string s) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
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
    private static MasterDataSet Md() => new(
        new[] { "EL" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<Street>(), new[] { "Blaulicht aus?" }, new[] { "Angriffstrupp" });

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

        Assert.Single(session.Incident.Journal);
        Assert.Single(vm.Etb.Entries);
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
        vm.Scba.NewMembers = "Müller / Schmidt";
        vm.Scba.AddTruppCommand.Execute(null);
        var row = vm.Scba.Trupps[^1];
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        Assert.True(vm.Scba.HasControlReminder);
        Assert.True(bar.IsVisible);
    }
}
