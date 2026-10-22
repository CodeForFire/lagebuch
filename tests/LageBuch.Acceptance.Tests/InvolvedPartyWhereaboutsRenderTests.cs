using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #701: where a Beteiligte/r is now, after the evacuation, typed on the BETEILIGTE card and shown
// with the time it was last changed. Doubles as the PR screenshot.
public class InvolvedPartyWhereaboutsRenderTests
{
    [AvaloniaFact]
    public void Leaving_the_aufenthaltsort_box_records_it_and_shows_its_stand()
    {
        var clock = new FixedClock { Now = new DateTimeOffset(2026, 6, 22, 21, 47, 0, TimeSpan.FromHours(2)) };
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Hauptstr. 12", 1, 2);
        session.AddInvolvedParty("Beispiel, Erika", "0171 0000001", "Mieterin, mit Kopfschmerzen angetroffen");
        session.AddInvolvedParty("Beispiel, Paul", null, "Sohn, ohne Beschwerden");
        session.AddInvolvedParty("Mustermann, Max", "0171 0000002", "Hausverwaltung, auf Anfahrt");
        var dwelling = session.Incident.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2);
        session.SetInvolvedPartyDwelling(session.Incident.InvolvedParties[0].Id, dwelling.Id);
        session.SetInvolvedPartyDwelling(session.Incident.InvolvedParties[1].Id, dwelling.Id);
        session.SetInvolvedPartyWhereabouts(session.Incident.InvolvedParties[1].Id, "bei Nachbarin Frau Meier, Hauptstr. 14");
        clock.Now = clock.Now.AddMinutes(9);

        var vm = new InvolvedPartiesViewModel(session, () => { });
        var view = new InvolvedPartiesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var boxes = view.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "WhereaboutsBox").ToList();
        Assert.Equal(3, boxes.Count);
        Assert.Equal("Aufenthaltsort", AutomationProperties.GetName(boxes[0]));
        boxes[0].Focus();
        Dispatcher.UIThread.RunJobs();
        window.Type("im RTW Rotkreuz Musterstadt 71/1");
        Assert.Null(session.Incident.InvolvedParties[0].Whereabouts); // only leaving the box commits

        boxes[2].Focus();
        Dispatcher.UIThread.RunJobs();

        var erika = session.Incident.InvolvedParties[0];
        Assert.Equal("im RTW Rotkreuz Musterstadt 71/1", erika.Whereabouts);
        Assert.Equal(clock.Now, erika.WhereaboutsAt);
        var stands = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Name == "WhereaboutsStand" && t.IsVisible)
            .Select(t => t.Text)
            .ToList();
        Assert.Equal(["Stand 21:56", "Stand 21:47"], stands);

        Capture(window, "beteiligte-aufenthaltsort.png");
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.Save(Path.Join(dir, name), PngBitmapEncoderOptions.Default);
    }
}
