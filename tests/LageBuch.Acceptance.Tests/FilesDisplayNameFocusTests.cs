using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// Same class of bug as ForcesNotesFocusTests (#33), for the Dateien list's display name field.
public class FilesDisplayNameFocusTests
{
    private static FilesViewModel BuildFilesVm(out LocalIncidentSession session, out FakeStore store)
    {
        store = new FakeStore();
        var clock = new FixedClock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        session = LocalIncidentSession.StartNew(
            store,
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 10);
        return new FilesViewModel(session, new FakeDialogs(), () => { });
    }

    [AvaloniaFact]
    public void Typing_in_the_displayname_cell_keeps_focus_on_the_same_textbox()
    {
        var vm = BuildFilesVm(out var session, out _);
        var view = new FilesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nameBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "brand.jpg");
        nameBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(nameBox.IsFocused, "DisplayName box did not receive focus to begin with.");

        foreach (var ch in "!!!")
        {
            window.KeyTextInput(ch.ToString());
            Dispatcher.UIThread.RunJobs();

            var focused = window.FocusManager?.GetFocusedElement();
            Assert.True(
                nameBox.IsFocused,
                $"DisplayName box lost focus after typing '{ch}'. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
        }

        // Mid-edit keystrokes must not push a half-typed name through to the domain — only a
        // commit (blur) does that. See Leaving_the_displayname_cell_commits_the_change_once below.
        Assert.Equal("brand.jpg", session.Incident.Files.Single().DisplayName);
    }

    [AvaloniaFact]
    public void Leaving_the_displayname_cell_commits_the_change_once()
    {
        var vm = BuildFilesVm(out var session, out var store);
        var view = new FilesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var saveCountBefore = store.SaveCount;

        var nameBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "brand.jpg");
        nameBox.Focus();
        Dispatcher.UIThread.RunJobs();
        nameBox.SelectAll();
        foreach (var ch in "Küche")
        {
            window.KeyTextInput(ch.ToString());
            Dispatcher.UIThread.RunJobs();
        }

        // Blur the field the way an operator tabbing away would.
        view.GetControl<Button>("AddFileButton").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Küche", session.Incident.Files.Single().DisplayName);

        // Five keystrokes committed as one edit -> exactly one save, not one per keystroke.
        Assert.Equal(saveCountBefore + 1, store.SaveCount);
    }
}
