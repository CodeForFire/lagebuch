using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

public class SituationReportsLayoutTests
{
    // The suggestion chevron of a Classes="field suggest" is pinned to the right edge of the
    // field's whole content. EINSATZSTELLE was the one suggest field whose content also held a
    // button, so the chevron landed on its ABC and drew "ABC⌄" over the button instead of
    // inside the box.
    [AvaloniaFact]
    public void Einsatzstelle_suggestion_chevron_sits_inside_the_box_and_clear_of_the_spell_button()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Musterfrau, Erika", "Florian Musterstadt 1/11-1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var reports = vm.EtbTabs.SituationReports;
        reports.NewReportCommand.Execute(null);
        var view = new SituationReportsView { DataContext = reports };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = view.GetControl<AutoCompleteBox>("LocationBox");
        var field = box.FindAncestorOfType<HeaderedContentControl>();
        Assert.NotNull(field);
        var chevron = field.GetVisualDescendants().OfType<PathIcon>()
            .Single(p => string.Equals(p.Name, "SuggestChevron", StringComparison.Ordinal));
        Assert.True(chevron.IsVisible, "EINSATZSTELLE lost its suggestion chevron");
        var spell = view.GetVisualDescendants().OfType<Button>()
            .Single(b => string.Equals(
                Avalonia.Automation.AutomationProperties.GetName(b), "Einsatzstelle buchstabieren", StringComparison.Ordinal));

        var boxRect = BoundsIn(box, view);
        var chevronRect = BoundsIn(chevron, view);
        var spellRect = BoundsIn(spell, view);

        Assert.True(
            chevronRect.Right <= boxRect.Right,
            $"chevron ends at {chevronRect.Right}, past the box's right edge at {boxRect.Right}");
        Assert.False(
            chevronRect.Intersects(spellRect),
            $"chevron {chevronRect} overlaps the ABC button {spellRect}");
    }

    private static Rect BoundsIn(Visual visual, Visual root)
    {
        var topLeft = visual.TranslatePoint(default, root);
        Assert.NotNull(topLeft);
        return new Rect(topLeft.Value, visual.Bounds.Size);
    }
}
