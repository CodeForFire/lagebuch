using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Behaviors;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// ::kürzel in the LAGEMELDUNG's text fields (#962), typed the way a Funker would. A Lagemeldung ends up
// in the ETB, so its fields offer the ETB's Textbausteine. The expansion has to reach the Funkspruch and
// the draft: the view writes the draft when a field is left, not on every keystroke.
public class SituationReportSnippetTests
{
    private static readonly TextSnippet[] Snippets =
    {
        new("fuk", "Feuer unter Kontrolle", "Lage", ForTasks: false),
        new("fua", "Feuer aus, Nachlöscharbeiten laufen", "Lage", ForTasks: false),

        // Matches "::fu" too, but is ticked for Aufgaben only: the Lagemeldung must not offer it.
        new("fuw", "Wärmebildkamera nachfordern", "Aufgabe", ForEtb: false),
    };

    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Muster", "Florian Musterstadt 11/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.SetAddress("Blumenstraße", "Musterstadt-Nord", "13", "12345", "Musterstadt");
        session.AddForceUnit("FF Musterstadt", 9, callSign: "HLF 1", officerCount: 1, scbaCount: 4);
        session.AddSituationReport(
            "Florian Musterstadt 11/1",
            "Blumenstraße 13",
            null,
            "Brandbekämpfung mit einem C-Hohlstrahlrohr",
            "FF Musterstadt (HLF 1) 0/1/8/9",
            null);
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData() with { TextSnippets = Snippets },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static TextBox FocusField(Window window, string name)
    {
        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == name);
        box.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        return box;
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaTheory]
    [InlineData("SituationBox")]
    [InlineData("MeasuresBox")]
    [InlineData("UnitsBox")]
    [InlineData("RequestsBox")]
    public void Every_lagemeldung_text_field_offers_the_etb_textbausteine(string field)
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusField(window, field);

        window.Type("::fu");

        Assert.Equal(new[] { "fuk", "fua" }, SnippetCompletion.OfferedSnippets(box).Select(s => s.Shortcut));
    }

    [AvaloniaFact]
    public void Typing_a_kuerzel_in_the_lage_opens_the_list()
    {
        var (window, _, _) = ShowWorkspace();
        var box = FocusField(window, "SituationBox");

        window.Type("Schuppen, ::fu");
        Capture(window, "lagemeldung-textbausteine.png");

        Assert.Equal(new[] { "fuk", "fua" }, SnippetCompletion.OfferedSnippets(box).Select(s => s.Shortcut));
        Assert.True(SnippetCompletion.ClaimsEnter(box));
    }

    [AvaloniaFact]
    public void Enter_expands_the_textbaustein_into_the_funkspruch_and_the_draft_without_sending_it()
    {
        var (window, vm, session) = ShowWorkspace();
        var box = FocusField(window, "SituationBox");
        var journal = session.Incident.Journal.Count;

        window.Type("Schuppen, ::fuk");
        window.Press(PhysicalKey.Enter);

        Assert.Equal("Schuppen, Feuer unter Kontrolle ", box.Text);
        Assert.Empty(SnippetCompletion.OfferedSnippets(box));
        window.AssertFocused(box);
        Assert.Contains("Schuppen, Feuer unter Kontrolle", vm.SituationReports.FunkspruchPreview, StringComparison.Ordinal);
        Assert.False(session.Incident.SituationReports[0].IsSent);
        Assert.Equal(journal, session.Incident.Journal.Count);

        // Leaving the field writes the draft, with the expansion in it.
        window.Press(PhysicalKey.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Schuppen, Feuer unter Kontrolle", session.Incident.SituationReports[0].Situation, StringComparison.Ordinal);
    }
}
