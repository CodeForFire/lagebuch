using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Regression for the crowded header reported with #472: an unnamed incident, shared, with the
// Lagebuchführer readout next to the sharing status. The header's *,Auto Grid gave the right-hand
// group all the width it asked for and drew the incident identity underneath it: at 1920px
// "+ Einsatzdaten ergänzen" sat under LAGEBUCHFÜHRER, at ~1340px the Stichwort, the operator and
// the share URLs were all drawn on top of each other. Whatever the width, no two header elements
// may overlap — the status group wraps onto a second line instead.
public class HeaderLayoutTests
{
    private sealed class FakeHost : IIncidentHostController
    {
        public bool CanHost => true;

        public bool IsHosting { get; private set; }

        public string? ShareHint => "Im Netzwerk: https://192.168.1.27:5859\nAuf diesem Gerät: https://localhost:5859";

        public string? SharePin => IsHosting ? "5393" : null;

        public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default)
        {
            IsHosting = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsHosting = false;
            return Task.CompletedTask;
        }
    }

    private static async Task<Window> ShowSharedUnnamedIncident(double width)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Maximilian Mustermann-Beispiel", "FFB 1/12/1"),
            "/x.fwincident",
            [("Aufstellort ELW weit genug weg um nicht zu behindern?", true)],
            [("Fahrzeug abgerüstet und einsatzbereit?", true)]);
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new ManualTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new FakeHost());

        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await vm.ToggleSharingCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Diagnostic only: set RENDER_OUT to a directory to get each width as a PNG (PR screenshots).
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static Rect BoundsInWindow(Window window, Control control)
    {
        var origin = control.TranslatePoint(default, window) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    [AvaloniaTheory]
    [InlineData(1920)]
    [InlineData(1340)]
    [InlineData(1024)]
    [InlineData(800)]
    public async Task No_two_header_elements_overlap(double width)
    {
        var window = await ShowSharedUnnamedIncident(width);
        Capture(window, $"header-{width}.png");

        string[] names =
        [
            "EinsatznummerValue", "AddIncidentDataButton", "OperatorReadout", "ShareInfoButton", "StatusReadout",
        ];
        var rects = names
            .Select(n => (Name: n, Control: window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == n)))
            .Where(x => x.Control.IsEffectivelyVisible)
            .Select(x => (x.Name, Rect: BoundsInWindow(window, x.Control)))
            .ToList();

        // Every one of them is on screen in this scenario (unnamed incident, operator set, shared).
        Assert.Equal(names.Length, rects.Count);

        for (var i = 0; i < rects.Count; i++)
        {
            Assert.True(
                rects[i].Rect.Right <= width + 1.0,
                $"{rects[i].Name} ends at {rects[i].Rect.Right:F0}px, past the {width}px window.");
            for (var j = i + 1; j < rects.Count; j++)
            {
                // Shrunk by a pixel so rounding at a shared edge is not reported as an overlap.
                var overlap = rects[i].Rect.Deflate(1).Intersect(rects[j].Rect.Deflate(1));
                Assert.True(
                    overlap.Width <= 0 || overlap.Height <= 0,
                    $"At {width}px {rects[i].Name} {rects[i].Rect} overlaps {rects[j].Name} {rects[j].Rect}.");
            }
        }
    }

    [AvaloniaFact]
    public async Task A_wide_window_keeps_the_header_on_one_line()
    {
        var window = await ShowSharedUnnamedIncident(1920);

        var line = window.GetVisualDescendants().OfType<LeadTrailPanel>().Single(p => p.Name == "HeaderIdentityLine");
        Assert.False(line.IsWrapped);
    }

    [AvaloniaFact]
    public async Task A_narrow_window_wraps_the_status_group_onto_its_own_line()
    {
        var window = await ShowSharedUnnamedIncident(800);

        var line = window.GetVisualDescendants().OfType<LeadTrailPanel>().Single(p => p.Name == "HeaderIdentityLine");
        Assert.True(line.IsWrapped);
    }
}
