using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #702: a Beteiligte/r linked to the Wohnung of the CO-Messung they were found in — picked on the
// BETEILIGTE card, and shown back in the Wohnung's editor. Doubles as the PR screenshots; the
// flyout is a popup, a TopLevel of its own, so its frame is drawn over the window's.
public class InvolvedPartyDwellingRenderTests
{
    private static LocalIncidentSession NewSession(FixedClock clock)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Hauptstr. 12", 1, 2);
        session.AddInvolvedParty("Beispiel, Erika", "0171 0000001", "Mieterin, mit Kopfschmerzen angetroffen");
        session.AddInvolvedParty("Mustermann, Max", "0171 0000002", "Hausverwaltung, auf Anfahrt");
        return session;
    }

    [AvaloniaFact]
    public void The_beteiligte_card_picks_the_wohnung_from_a_flyout()
    {
        var clock = new FixedClock();
        var session = NewSession(clock);
        var vm = new InvolvedPartiesViewModel(session, () => { });
        var view = new InvolvedPartiesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 620 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DwellingButton");
        Assert.Equal(DwellingChoice.NoneDisplay, button.Content);
        Assert.Equal("Wohnung wählen", AutomationProperties.GetName(button));

        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Capture(window, button, "beteiligte-wohnung-flyout.png");

        var pick = ((Flyout)button.Flyout).Content is Control content
            ? content.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is DwellingChoice { Display: "Hauptstr. 12, 1. OG, Whg. 2" })
            : throw new InvalidOperationException("the flyout has no content");
        pick.Command!.Execute(null);
        button.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", session.Incident.DwellingLocationOf(session.Incident.InvolvedParties[0]));
        var after = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DwellingButton");
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", after.Content);
        Capture(window, null, "beteiligte-wohnung-after.png");
    }

    [AvaloniaFact]
    public void The_wohnung_editor_lists_who_was_found_there()
    {
        var clock = new FixedClock();
        var session = NewSession(clock);
        var dwelling = session.Incident.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2);
        session.SetInvolvedPartyDwelling(session.Incident.InvolvedParties[0].Id, dwelling.Id);
        session.SetDwellingStatus(dwelling.BuildingId, 1, 2, Domain.CoMeasurement.DwellingStatus.Affected);
        var vm = new CoMessprotokollViewModel(session, clock, () => { });
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.MatrixRows.Single(r => r.Ordinal == 1).Cells.Single(c => c.ApartmentNumber == 2).OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var panel = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DwellingInvolvedParties");
        Assert.True(panel.IsVisible);
        Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Beispiel, Erika");
        Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Mustermann, Max");
        Capture(window, null, "co-wohnung-beteiligte.png");
    }

    private static void Capture(Window window, Button? flyoutOwner, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
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
