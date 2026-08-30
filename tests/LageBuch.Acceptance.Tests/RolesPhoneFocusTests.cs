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

// Reproduces the focus-loss reported against the Funktionen Handynummer cell: typing must not
// knock focus out of the field after every character.
public class RolesPhoneFocusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
    };

    private static RolesViewModel BuildRolesVm(out LocalIncidentSession session)
    {
        session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Müller", phone: "0171");
        return new RolesViewModel(session, new FixedClock(), Md(), () => { });
    }

    [AvaloniaFact]
    public void Typing_in_the_phone_cell_keeps_focus_on_the_same_textbox()
    {
        var vm = BuildRolesVm(out var session);
        var view = new RolesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var phoneBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "0171");
        phoneBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(phoneBox.IsFocused, "Phone box did not receive focus to begin with.");

        foreach (var ch in "2345")
        {
            window.KeyTextInput(ch.ToString());
            Dispatcher.UIThread.RunJobs();

            var focused = window.FocusManager?.GetFocusedElement();
            Assert.True(
                phoneBox.IsFocused,
                $"Phone box lost focus after typing '{ch}'. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
        }

        // Mid-edit keystrokes must not push a half-typed number through to the domain/ETB —
        // only a commit (blur) does that. See Leaving_the_phone_cell_commits_the_change_once below.
        Assert.Equal("0171", Assert.Single(session.Incident.Roles).Phone);
    }

    [AvaloniaFact]
    public void Leaving_the_phone_cell_commits_the_change_once()
    {
        var vm = BuildRolesVm(out var session);
        var view = new RolesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var journalCountBefore = session.Incident.Journal.Count;

        var phoneBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "0171");
        phoneBox.Focus();
        Dispatcher.UIThread.RunJobs();
        phoneBox.SelectAll();
        window.KeyTextInput("0172");
        Dispatcher.UIThread.RunJobs();

        // Blur the field the way an operator tabbing away would.
        view.GetControl<AutoCompleteBox>("RoleBox").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("0172", Assert.Single(session.Incident.Roles).Phone);

        // One committed edit -> exactly one ETB line, not one per keystroke.
        Assert.Equal(journalCountBefore + 1, session.Incident.Journal.Count);
    }
}
