using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Reporting what is missing must not cost the dock any width (#412). It did: a message under a
// field made that field as wide as the message -- a horizontal StackPanel measures its children at
// infinite width, so TextWrapping never fires -- and pushed HINZUFÜGEN a further 181 px to the
// right, off the edge of the window. Nothing catches that: there is no horizontal scrollbar
// between a dock and the window, and the tab content host clips, so the button was simply gone.
//
// The assertion is invariance rather than "fits in the viewport", because the Kräfte dock is a row
// of fixed-width fields totalling ~1260 px and has never fitted a narrow window -- that is a
// separate, pre-existing limitation (see CommandBarReachabilityTests for the same shape of problem
// on the command bar). What this pins is that the messages are free: the button lands in exactly
// the same place whether the dock is quiet or reporting every rule at once.
public class DockReachabilityTests
{
    private const double Width = 1460.0;
    private const double Height = 620.0;

    [AvaloniaFact]
    public void Reporting_what_is_missing_does_not_move_the_Kraefte_add_button()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(
            session,
            new FixedClock(),
            MasterDataSet.Empty with { Vehicles = AnonymizedExampleData.Vehicles },
            () => { });
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = Width, Height = Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = ButtonNamed(view, "AddForceButton");
        var quiet = HorizontalBounds(button, window);

        // Every rule fails at once -- the worst case for a dock that grows with its messages.
        vm.NewScbaCount = 3;
        vm.AddForceCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.ErrorSummary); // the dock really is reporting, not silently passing
        var reporting = HorizontalBounds(button, window);

        // Evidence in the failure message: how far the messages shoved the button.
        var message = $"HINZUFÜGEN sat at x=[{quiet.Left:0}..{quiet.Right:0}] on a quiet dock and at " +
            $"x=[{reporting.Left:0}..{reporting.Right:0}] once it reported — the messages moved it " +
            $"{reporting.Right - quiet.Right:0} px and can push it out of the window entirely.";
        Assert.Equal(quiet.Right, reporting.Right, precision: 0);
        Assert.True(Math.Abs(reporting.Right - quiet.Right) <= 1.0, message);
    }

    private static Button ButtonNamed(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);

    private static (double Left, double Right) HorizontalBounds(Visual v, Visual relativeTo)
    {
        var left = v.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;
        return (left, left + v.Bounds.Width);
    }
}
