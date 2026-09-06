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

// Same "row rebuilds on each commit" trap as #33 (RolesPhoneFocusTests, ForcesNotesFocusTests,
// FilesDisplayNameFocusTests): typing in the CO-Messung apartment header pushed straight to the
// session on every keystroke, whose Changed event rebuilds ApartmentColumns as a fresh array of
// fresh view models, tearing down and recreating the TextBox mid-edit.
public class CoMessprotokollHeaderFocusTests
{
    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm) BuildVm()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            Path.GetTempFileName(),
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 2, 3);
        var vm = new CoMessprotokollViewModel(session, new FixedClock(), () => { });
        return (session, vm);
    }

    [AvaloniaFact]
    public void Typing_in_the_apartment_header_keeps_focus_on_the_same_textbox()
    {
        var (session, vm) = BuildVm();
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var headerBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "Links");
        headerBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(headerBox.IsFocused, "Header box did not receive focus to begin with.");

        foreach (var ch in "!!!")
        {
            window.KeyTextInput(ch.ToString());
            Dispatcher.UIThread.RunJobs();

            var focused = window.FocusManager?.GetFocusedElement();
            Assert.True(
                headerBox.IsFocused,
                $"Header box lost focus after typing '{ch}'. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
        }

        // Mid-edit keystrokes must not push a half-typed label through to the domain — only a
        // commit (blur) does that. See Leaving_the_apartment_header_commits_the_change_once below.
        Assert.Equal("Links", session.Incident.Buildings[0].ApartmentLabels.GetValueOrDefault(1, "Links"));
    }

    [AvaloniaFact]
    public void Leaving_the_apartment_header_commits_the_change_once()
    {
        var (session, vm) = BuildVm();
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var headerBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "Links");
        headerBox.Focus();
        Dispatcher.UIThread.RunJobs();
        headerBox.SelectAll();
        window.KeyTextInput("Vorne");
        Dispatcher.UIThread.RunJobs();

        // Blur the field the way an operator tabbing away would.
        view.GetVisualDescendants().OfType<ComboBox>().First().Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Vorne", session.Incident.Buildings[0].ApartmentLabels[1]);
    }
}
