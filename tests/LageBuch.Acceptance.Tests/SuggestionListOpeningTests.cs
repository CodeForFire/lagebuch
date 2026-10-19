using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #259: a user who doesn't already know a valid entry must be able to browse the full (unfiltered)
// Stammdaten list without typing. A click into the field or ↓ opens it. Focus alone used to open it
// too, which the field test turned down: a module switch drops the list over the very rows the
// Lagebuchführer came to read. The box now opens the way the KRÄFTE ComboBox does.
public class SuggestionListOpeningTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { new Role("EL"), new Role("GF") },
    };

    private static RolesViewModel BuildRolesVm()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return new RolesViewModel(session, new FixedClock(), Md(), () => { });
    }

    [AvaloniaFact]
    public void Focusing_the_role_box_leaves_its_suggestion_list_closed()
    {
        var view = new RolesView { DataContext = BuildRolesVm() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var roleBox = view.GetControl<AutoCompleteBox>("RoleBox");
        roleBox.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.False(roleBox.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void Arrow_down_in_the_focused_role_box_opens_the_full_list()
    {
        var view = new RolesView { DataContext = BuildRolesVm() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var roleBox = view.GetControl<AutoCompleteBox>("RoleBox");
        roleBox.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.ArrowDown);

        Assert.True(roleBox.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void Clicking_the_focused_role_box_opens_its_list()
    {
        var view = new RolesView { DataContext = BuildRolesVm() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var roleBox = view.GetControl<AutoCompleteBox>("RoleBox");
        roleBox.Focus();
        Dispatcher.UIThread.RunJobs();

        var center = Avalonia.VisualExtensions.TranslatePoint(
            roleBox, new Avalonia.Point(roleBox.Bounds.Width / 2, roleBox.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(roleBox.IsDropDownOpen);
    }

    // The app's recent-files ListBoxItem used to reach into the popup and draw each suggestion as a
    // spaced card in the mono font. The rows are now a ComboBox list's: body font, no gap between them.
    [AvaloniaFact]
    public void The_open_list_draws_its_rows_like_a_combo_box_list()
    {
        var view = new RolesView { DataContext = BuildRolesVm() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var roleBox = view.GetControl<AutoCompleteBox>("RoleBox");
        roleBox.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.ArrowDown);

        var popup = roleBox.GetVisualDescendants().OfType<Popup>().Single();
        var row = Assert.IsAssignableFrom<Visual>(popup.Child).GetVisualDescendants().OfType<ListBoxItem>().First();
        Assert.Equal(roleBox.FontFamily, row.FontFamily);
        Assert.Equal(default, row.Margin);
        Assert.Equal(new Thickness(11, 8), row.Padding);
    }
}
