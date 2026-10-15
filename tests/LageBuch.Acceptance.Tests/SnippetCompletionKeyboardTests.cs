using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Behaviors;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// ::kürzel in the text fields. Every test types and presses keys the way a Funker would; none sets
// a view model property it could have typed. The point of most of them is what a key does NOT do
// while the list is open: Enter must not submit the dock, Esc must not close the dialog. And the
// two lists never mix: an ETB field offers only the ETB's Textbausteine, an Aufgabe field only the
// Aufgaben's -- both lists below start with "st" on purpose.
public class SnippetCompletionKeyboardTests
{
    private static readonly TextSnippet[] Snippets =
    {
        new("stl", "Stromleitung beschädigt, Bereich abgesperrt, EVU alarmiert", "Sonderfall"),
        new("std", "Straße gesperrt, Umleitung über [ORT]", "Anfahrt"),
        new("lgb", "Lage bestätigt: [X]", "Erkundung"),
    };

    private static readonly TextSnippet[] AufgabenSnippets =
    {
        new("stw", "Stadtwerke verständigen", "Versorger"),
        new("spe", "Bereich [X] absperren", "Sicherung"),
    };

    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData() with { TextSnippets = Snippets, TaskSnippets = AufgabenSnippets },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static TextBox FocusEtbText(Window window)
    {
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var box = Named<TextBox>(window, "EtbTextBox");
        box.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        return box;
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void Typing_a_kuerzel_offers_the_matching_textbausteine()
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusEtbText(window);

        window.Type("Baum auf Fahrbahn ::st");
        Capture(window, "textbausteine-etb.png");

        // Kürzel matches first, then a text that merely contains "st" ("bestätigt").
        Assert.Equal(new[] { "stl", "std", "lgb" }, SnippetCompletion.OfferedSnippets(box).Select(s => s.Shortcut));
        Assert.Equal(0, SnippetCompletion.SelectedIndex(box));
    }

    [AvaloniaFact]
    public void Enter_inserts_the_textbaustein_and_does_not_submit_the_etb_entry()
    {
        var (window, vm, _) = ShowWorkspace();
        var box = FocusEtbText(window);
        var before = vm.Etb.Entries.Count;

        window.Type("Baum auf Fahrbahn, ::stl");
        window.Press(PhysicalKey.Enter);

        Assert.Equal("Baum auf Fahrbahn, Stromleitung beschädigt, Bereich abgesperrt, EVU alarmiert ", box.Text);
        Assert.Equal(before, vm.Etb.Entries.Count);
        Assert.Empty(SnippetCompletion.OfferedSnippets(box));
        window.AssertFocused(box);

        // The list is gone, so the next Enter is the form's again.
        window.Press(PhysicalKey.Enter);
        Assert.Equal(before + 1, vm.Etb.Entries.Count);
        Assert.Equal("Baum auf Fahrbahn, Stromleitung beschädigt, Bereich abgesperrt, EVU alarmiert", vm.Etb.Entries[0].Text);
    }

    [AvaloniaFact]
    public void The_first_gap_is_selected_so_typing_fills_it()
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusEtbText(window);

        window.Type("::lgb");
        window.Press(PhysicalKey.Enter);

        Capture(window, "textbausteine-luecke.png");
        Assert.Equal("[X]", box.SelectedText);
        window.Type("Zimmerbrand 2. OG");
        Assert.Equal("Lage bestätigt: Zimmerbrand 2. OG ", box.Text);
    }

    [AvaloniaFact]
    public void Down_chooses_the_next_row_and_tab_takes_it()
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusEtbText(window);

        window.Type("::st");
        window.Press(PhysicalKey.ArrowDown);
        Assert.Equal(1, SnippetCompletion.SelectedIndex(box));
        window.Press(PhysicalKey.Tab);

        Assert.Equal("Straße gesperrt, Umleitung über [ORT] ", box.Text);
        Assert.Equal("[ORT]", box.SelectedText);
        window.AssertFocused(box);
    }

    [AvaloniaFact]
    public void Esc_closes_the_list_and_leaves_the_typed_text()
    {
        var (window, vm, _) = ShowWorkspace();
        var box = FocusEtbText(window);
        var before = vm.Etb.Entries.Count;

        window.Type("Funk ::st");
        window.Press(PhysicalKey.Escape);

        Assert.Equal("Funk ::st", box.Text);
        Assert.Empty(SnippetCompletion.OfferedSnippets(box));
        Assert.Equal(before, vm.Etb.Entries.Count);
    }

    [AvaloniaFact]
    public void A_space_ends_the_request_and_the_list_closes()
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusEtbText(window);

        window.Type("::st");
        Assert.NotEmpty(SnippetCompletion.OfferedSnippets(box));
        window.Type(" ");

        Assert.Empty(SnippetCompletion.OfferedSnippets(box));
    }

    [AvaloniaFact]
    public void In_the_aufgabe_dialog_esc_closes_only_the_list_and_enter_only_inserts()
    {
        var (window, vm, session) = ShowWorkspace();
        vm.Etb.NewText = "Stromleitung hängt tief";
        vm.Etb.AddEntryAndCreateTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.IsType<TaskDialogViewModel>(vm.PendingTaskDialog);
        var box = Named<TextBox>(window, "TaskDialogText");
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
        Dispatcher.UIThread.RunJobs();

        window.Type("::s");
        Assert.Equal(new[] { "stw", "spe" }, SnippetCompletion.OfferedSnippets(box).Select(t => t.Shortcut));
        window.Press(PhysicalKey.Escape);
        Assert.Same(dialog, vm.PendingTaskDialog);

        window.Type("pe");
        window.Press(PhysicalKey.Enter);
        Assert.Same(dialog, vm.PendingTaskDialog);
        Assert.Equal("Bereich [X] absperren ", box.Text);
        Assert.Equal("[X]", box.SelectedText);
        Assert.Empty(session.Incident.Tasks);
    }

    [AvaloniaFact]
    public void Without_textbausteine_a_double_colon_is_just_text()
    {
        var (window, _, session) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUFGABEN");
        var box = Named<TextBox>(window, "TaskNewTextBox");
        SnippetCompletion.SetSnippets(box, Array.Empty<TextSnippet>());
        box.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Type("Uhrzeit 14::st");
        window.Press(PhysicalKey.Enter);

        Assert.Equal("Uhrzeit 14::st", Assert.Single(session.Incident.Tasks).Text);
    }

    // The edit panel saves on an Enter KeyBinding and cancels on the inline overlay's Esc. Both
    // only see keys nobody handled, so the open list keeps them -- the correction is not saved
    // half-written, and Esc does not throw away what was typed.
    [AvaloniaFact]
    public void In_the_etb_edit_panel_enter_inserts_and_esc_closes_only_the_list()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddJournalEntry(EtbDirection.Incoming, "Baum auf Fahrbahn", from: "ILS");
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        vm.Etb.Entries.First(e => e.Text == "Baum auf Fahrbahn").BeginEditCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var box = Named<TextBox>(window, "EditTextBox");
        box.Focus(NavigationMethod.Tab);
        box.CaretIndex = box.Text!.Length;
        Dispatcher.UIThread.RunJobs();

        window.Type(", ::st");
        window.Press(PhysicalKey.Escape);
        Assert.True(vm.Etb.IsEditing);

        window.Type("l");
        window.Press(PhysicalKey.Enter);
        Assert.True(vm.Etb.IsEditing);
        Assert.Equal("Baum auf Fahrbahn, Stromleitung beschädigt, Bereich abgesperrt, EVU alarmiert ", box.Text);
        Assert.Contains(session.Incident.Journal, e => e.Text == "Baum auf Fahrbahn");
    }

    [AvaloniaFact]
    public void An_aufgabe_field_offers_only_the_aufgaben_textbausteine()
    {
        var (window, _, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUFGABEN");
        var box = Named<TextBox>(window, "TaskNewTextBox");
        box.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Type("::st");
        Capture(window, "textbausteine-aufgaben.png");

        // stl and std are the ETB's: a Rückmeldung is not an Aufgabe.
        Assert.Equal(new[] { "stw" }, SnippetCompletion.OfferedSnippets(box).Select(t => t.Shortcut));

        window.Type("l");
        Assert.Empty(SnippetCompletion.OfferedSnippets(box));
    }

    [AvaloniaFact]
    public void An_etb_field_offers_only_the_etb_textbausteine()
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusEtbText(window);

        window.Type("::stw");

        Assert.Empty(SnippetCompletion.OfferedSnippets(box));
    }
}
