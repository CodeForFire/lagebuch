using System.Diagnostics;
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
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #971: the ETB's Einsatzabschnitt picker in the entry line, the quick filter above the grid and
// the Abschnitt badge on a filed row. With RENDER_OUT set it also writes the PR screenshots.
public class EtbSectionStripTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(bool withAbschnitte)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        if (withAbschnitte)
        {
            session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord");
            session.AddCommandNode(CommandNodeKind.Abschnitt, "Süd");
            var sued = session.Incident.CommandNodes.Single(n => n.Name == "Süd").Id;
            session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Tiefgarage", sued);
            session.AddCommandNode(CommandNodeKind.Bereitstellung, "Festplatz");
        }

        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            MasterDataSet.Empty with { Vehicles = AnonymizedExampleData.Vehicles },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static List<Button> Chips(Window window, string strip) =>
        [.. Named<ItemsControl>(window, strip).GetVisualDescendants().OfType<Button>()];

    // Picks an Abschnitt in the entry line's dropdown, by label.
    private static void Pick(Window window, string label)
    {
        var picker = Named<NodeChoicePicker>(window, "SectionEntryPicker");
        Assert.NotNull(picker.Choices);
        picker.Choices.Single(c => c.Label == label).SelectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Add(IncidentWorkspaceViewModel vm, string from, string text)
    {
        vm.Etb.NewFrom = from;
        vm.Etb.NewText = text;
        vm.Etb.AddEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Without_abschnitte_neither_strip_shows()
    {
        var (window, _, _) = ShowWorkspace(withAbschnitte: false);

        Assert.False(Named<DockPanel>(window, "SectionEntryBar").IsEffectivelyVisible);
        Assert.False(Named<DockPanel>(window, "SectionFilterBar").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_entry_line_dropdown_files_the_entry_and_the_row_shows_its_badge()
    {
        var (window, vm, session) = ShowWorkspace(withAbschnitte: true);

        var picker = Named<NodeChoicePicker>(window, "SectionEntryPicker");
        Assert.True(picker.IsEffectivelyVisible);
        Assert.Equal(NodeChoices.UnplacedLabel, picker.Label);
        Assert.Equal(
            [NodeChoices.UnplacedLabel, "Nord", "Süd", "Süd / Tiefgarage"],
            picker.Choices?.Select(c => c.Label));
        Assert.True(Named<DockPanel>(window, "SectionFilterBar").IsEffectivelyVisible);

        Pick(window, "Nord");
        Assert.Equal("Nord", picker.Label);
        Capture(window, "etb-abschnitt-eingabe.png");
        Add(vm, AnonymizedExampleData.CallSign, "Riegelstellung zum Nachbargebäude aufgebaut");

        var filed = session.Incident.Journal[^1];
        Assert.Equal("Nord", filed.SectionName);
        var badges = Named<DataGrid>(window, "EtbGrid").GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("section-badge") && b.IsEffectivelyVisible)
            .Select(b => (b.Child as TextBlock)?.Text)
            .ToList();
        Assert.Equal(["Nord"], badges);
    }

    [AvaloniaFact]
    public void The_quick_filter_narrows_the_grid_to_one_abschnitt()
    {
        var (window, vm, _) = ShowWorkspace(withAbschnitte: true);
        Pick(window, "Nord");
        Add(vm, AnonymizedExampleData.CallSign, "Riegelstellung zum Nachbargebäude aufgebaut");
        Pick(window, "Süd");
        Add(vm, AnonymizedExampleData.SecondCallSign, "Brand in der Tiefgarage, Rauch im Treppenraum");
        Pick(window, NodeChoices.UnplacedLabel); // the next entry belongs to no Abschnitt
        Add(vm, AnonymizedExampleData.CallSign, "Wasserversorgung über zwei B-Leitungen sichergestellt");

        var filters = Chips(window, "SectionFilterStrip");
        Assert.Equal(["Alle", "Nord", "Süd", "Süd / Tiefgarage"], filters.Select(b => b.Content as string));
        Assert.Contains("selected", filters[0].Classes);
        Capture(window, "etb-abschnitt-alle.png");

        filters[2].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Brand in der Tiefgarage, Rauch im Treppenraum"], vm.Etb.Entries.Select(e => e.Text));
        Assert.Contains("selected", filters[2].Classes);
        Assert.DoesNotContain("selected", filters[0].Classes);
        Capture(window, "etb-abschnitt-filter.png");
    }

    [AvaloniaFact]
    public void The_edit_panel_offers_the_abschnitt_picker()
    {
        var (window, vm, _) = ShowWorkspace(withAbschnitte: true);
        Pick(window, "Nord");
        Add(vm, AnonymizedExampleData.CallSign, "Riegelstellung zum Nachbargebäude aufgebaut");

        vm.Etb.Entries[0].BeginEditCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var picker = Named<NodeChoicePicker>(window, "EditSectionPicker");
        Assert.True(picker.IsEffectivelyVisible);
        Assert.Equal("Nord", picker.Label);
        Capture(window, "etb-abschnitt-bearbeiten.png");
    }

    // Only with RENDER_OUT set. Settles the buttons' 150 ms brush transitions first, which run on
    // the wall clock in the headless harness.
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 250)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.SavePng(Path.Join(dir, name));
    }
}
