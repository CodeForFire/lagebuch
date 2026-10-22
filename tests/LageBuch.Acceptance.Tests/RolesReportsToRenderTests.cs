using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #703: MELDET AN in the Funktionen grid and on the phone's cards. Doubles as the PR screenshots;
// the flyout is a popup, a TopLevel of its own, so its frame is drawn over the window's.
public class RolesReportsToRenderTests
{
    private static (Window Window, RolesView View, LocalIncidentSession Session) Show(bool narrow)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Huber", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Mayr", callSign: "FFB 1/1", from: clock.Now, phone: "0171 2345678");
        session.AssignRole("Abschnittsleiter", "Bauer", callSign: "FFB 40/1", from: clock.Now, section: "Abschnitt Nord");
        session.AssignRole("GF", "Wagner", callSign: "FFB 44/1", from: clock.Now, section: "Abschnitt Nord");
        var el = session.Incident.Roles[0];
        session.SetRoleReportsTo(session.Incident.Roles[1].Id, el.Id);
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        vm.Roles.IsNarrow = narrow;
        var view = new RolesView { DataContext = vm.Roles };
        var root = new Panel { Name = "WorkspaceRoot", Children = { view } };
        root.Classes.Set("narrow", narrow);
        var window = new Window { Content = root, Width = narrow ? 412 : 1600, Height = narrow ? 915 : 520 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, session);
    }

    [AvaloniaFact]
    public void Meldet_an_names_the_superior_and_its_flyout_picks_a_new_one()
    {
        var (window, view, session) = Show(narrow: false);
        var buttons = view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "ReportsToButton").ToList();
        Assert.Equal(["— niemand —", "EL (Mayr)", "— niemand —"], buttons.Select(b => b.Content as string));

        var gfButton = buttons[2];
        gfButton.Flyout!.ShowAt(gfButton);
        Dispatcher.UIThread.RunJobs();
        Capture(window, gfButton, "funktionen-meldet-an-flyout.png");

        var choices = ((Flyout)gfButton.Flyout).Content as ItemsControl;
        var pick = choices!.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is ReportsToChoice { Display: "Abschnittsleiter (Bauer)" });
        pick.Command!.Execute(null);
        gfButton.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(session.Incident.Roles[1].Id, session.Incident.Roles[2].ReportsToId);
        var after = view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "ReportsToButton").Select(b => b.Content as string);
        Assert.Equal(["— niemand —", "EL (Mayr)", "Abschnittsleiter (Bauer)"], after);
        Capture(window, null, "funktionen-meldet-an-after.png");
    }

    [AvaloniaFact]
    public void A_phone_card_shows_meldet_an_with_an_automation_name()
    {
        var (window, view, _) = Show(narrow: true);

        var cardButtons = view.GetControl<ItemsControl>("RolesCards").GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "ReportsToCardButton").ToList();

        Assert.Equal(3, cardButtons.Count);
        Assert.Equal("EL (Mayr)", cardButtons[1].Content);
        Assert.All(cardButtons, b => Assert.Equal("Meldet an wählen", Avalonia.Automation.AutomationProperties.GetName(b)));
        Capture(window, null, "funktionen-meldet-an-phone.png");
    }

    private static void Capture(Window window, Button? flyoutOwner, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (dir is null)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var windowFrame = window.CaptureRenderedFrame()!;
        using var composite = new RenderTargetBitmap(windowFrame.PixelSize);
        using (var context = composite.CreateDrawingContext())
        {
            context.DrawImage(windowFrame, new Rect(windowFrame.Size));
            if (flyoutOwner?.Flyout is Flyout { Content: Control content } && TopLevel.GetTopLevel(content) is { } popupRoot
                && !ReferenceEquals(popupRoot, window))
            {
                using var popupFrame = popupRoot.CaptureRenderedFrame()!;
                var offset = popupRoot.PointToScreen(default) - window.PointToScreen(default);
                context.DrawImage(popupFrame, new Rect(new Point(offset.X, offset.Y), popupFrame.Size));
            }
        }

        composite.Save(Path.Join(dir, name), PngBitmapEncoderOptions.Default);
    }
}
