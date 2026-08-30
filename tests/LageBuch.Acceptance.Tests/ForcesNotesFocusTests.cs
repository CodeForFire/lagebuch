using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Reproduces issue #33: typing in the Bemerkung cell saved the whole incident file on every
// keystroke. Mirrors RolesPhoneFocusTests for the Handynummer fix.
public class ForcesNotesFocusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Brigades = new[] { "FFB Wache 1" },
        UnitStatus = new[] { "Alarmiert" },
    };

    private static ForcesViewModel BuildForcesVm(out LocalIncidentSession session, out FakeStore store)
    {
        store = new FakeStore();
        session = LocalIncidentSession.StartNew(
            store,
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddForceUnit("FFB Wache 1", 9, null, "Alarmiert", "erste Meldung");
        return new ForcesViewModel(session, new FixedClock(), Md(), () => { });
    }

    [AvaloniaFact]
    public void Typing_in_the_bemerkung_cell_keeps_focus_on_the_same_textbox()
    {
        var vm = BuildForcesVm(out var session, out _);
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var notesBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "erste Meldung");
        notesBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(notesBox.IsFocused, "Bemerkung box did not receive focus to begin with.");

        foreach (var ch in "!!!")
        {
            window.KeyTextInput(ch.ToString());
            Dispatcher.UIThread.RunJobs();

            var focused = window.FocusManager?.GetFocusedElement();
            Assert.True(
                notesBox.IsFocused,
                $"Bemerkung box lost focus after typing '{ch}'. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
        }

        // Mid-edit keystrokes must not push a half-typed note through to the domain — only a
        // commit (blur) does that. See Leaving_the_bemerkung_cell_commits_the_change_once below.
        Assert.Equal("erste Meldung", session.Incident.Forces[0].Notes);
    }

    [AvaloniaFact]
    public void Leaving_the_bemerkung_cell_commits_the_change_once()
    {
        var vm = BuildForcesVm(out var session, out var store);
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var saveCountBefore = store.SaveCount;

        var notesBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "erste Meldung");
        notesBox.Focus();
        Dispatcher.UIThread.RunJobs();
        notesBox.SelectAll();
        foreach (var ch in "Alarm")
        {
            window.KeyTextInput(ch.ToString());
            Dispatcher.UIThread.RunJobs();
        }

        // Blur the field the way an operator tabbing away would.
        view.GetControl<AutoCompleteBox>("BrigadeBox").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Alarm", session.Incident.Forces[0].Notes);

        // Five keystrokes committed as one edit -> exactly one save, not one per keystroke.
        Assert.Equal(saveCountBefore + 1, store.SaveCount);
    }
}
