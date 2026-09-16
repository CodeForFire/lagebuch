using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #259: an AutoCompleteBox's suggestion dropdown used to open only after the first keystroke, so a
// user who didn't already know a valid entry had no way to browse the list. Focusing or clicking
// the field should open it immediately, showing the full (unfiltered) Stammdaten list.
public class OpenDropDownOnFocusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL", "GF" },
    };

    private static RolesViewModel BuildRolesVm()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return new RolesViewModel(session, new FixedClock(), Md(), () => { });
    }

    [AvaloniaFact]
    public void Focusing_the_role_box_opens_its_suggestion_dropdown()
    {
        var view = new RolesView { DataContext = BuildRolesVm() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var roleBox = view.GetControl<AutoCompleteBox>("RoleBox");
        Assert.False(roleBox.IsDropDownOpen, "Dropdown should start closed.");

        roleBox.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(roleBox.IsDropDownOpen, "Focusing the field should open the suggestion dropdown without requiring a keystroke.");
    }

    [AvaloniaFact]
    public void Clicking_the_role_box_reopens_the_dropdown_after_it_was_closed()
    {
        var view = new RolesView { DataContext = BuildRolesVm() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var roleBox = view.GetControl<AutoCompleteBox>("RoleBox");
        roleBox.Focus();
        Dispatcher.UIThread.RunJobs();
        roleBox.IsDropDownOpen = false; // e.g. the user pressed Escape
        Dispatcher.UIThread.RunJobs();

        var center = Avalonia.VisualExtensions.TranslatePoint(
            roleBox, new Avalonia.Point(roleBox.Bounds.Width / 2, roleBox.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(roleBox.IsDropDownOpen, "Clicking an already-focused field should reopen the dropdown.");
    }
}
