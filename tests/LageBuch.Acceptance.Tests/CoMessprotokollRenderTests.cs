using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

public class CoMessprotokollRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
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
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void CoMessprotokoll_Tab_Renders()
    {
        var (window, vm, session) = ShowWorkspace();

        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        session.AddCoBuilding("Mehrfamilienhaus B", 2, 3);
        Dispatcher.UIThread.RunJobs();

        var buildingA = session.Incident.Buildings[0];
        session.RecordCoValue(buildingA.Id, 2, 1, 45);
        session.SetDwellingStatus(buildingA.Id, 2, 1, Domain.CoMeasurement.DwellingStatus.Affected);
        session.RecordCoValue(buildingA.Id, 2, 2, 120);
        session.SetDwellingStatus(buildingA.Id, 2, 2, Domain.CoMeasurement.DwellingStatus.Searched);
        session.RecordCoValue(buildingA.Id, 1, 1, 8);
        session.SetDwellingStatus(buildingA.Id, 1, 1, Domain.CoMeasurement.DwellingStatus.Searched);

        // ppm danger coloring is independent of Status: these two stay NotSearched so the render
        // shows the Dangerous/Lethal ppm coloring on tiles that haven't been marked yet either.
        session.RecordCoValue(buildingA.Id, 2, 3, 250);
        session.RecordCoValue(buildingA.Id, 2, 4, 900);
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");

        Assert.Equal(2, vm.CoMessprotokoll.BuildingOptions.Count);
        Assert.NotNull(vm.CoMessprotokoll.SelectedBuilding);
        Assert.NotEmpty(vm.CoMessprotokoll.MatrixRows);

        Capture(window, "co-messung.png");
    }

    [AvaloniaFact]
    public void CoMessprotokoll_handed_over_key_shows_its_Schluesselnummer_on_tile_and_in_editor()
    {
        var (window, vm, session) = ShowWorkspace();

        session.AddCoBuilding("Mehrfamilienhaus A", 2, 3);
        Dispatcher.UIThread.RunJobs();

        var haus = session.Incident.Buildings[0].Id;
        session.SetDwellingDetails(haus, 2, 1, "Musterfrau", Domain.CoMeasurement.KeyAvailability.Available, "14");
        session.SetDwellingDetails(haus, 1, 2, null, Domain.CoMeasurement.KeyAvailability.Available, "B-3");
        session.SetDwellingDetails(haus, 1, 3, null, Domain.CoMeasurement.KeyAvailability.Available);
        session.SetDwellingDetails(haus, 0, 1, "Mustermann", Domain.CoMeasurement.KeyAvailability.NotAvailable);
        session.RecordCoValue(haus, 2, 1, 45);
        session.SetDwellingStatus(haus, 2, 1, Domain.CoMeasurement.DwellingStatus.Affected);
        session.SetDwellingStatus(haus, 1, 2, Domain.CoMeasurement.DwellingStatus.Searched);
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");

        var cell = vm.CoMessprotokoll.MatrixRows.SelectMany(r => r.Cells).Single(c => c.FloorOrdinal == 2 && c.ApartmentNumber == 1);
        Assert.Equal("\uD83D\uDD11 14", cell.KeyDisplay);
        cell.OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "KeyNumberBox");
        Assert.True(box.IsEnabled);
        Assert.Equal("14", box.Text);

        vm.CoMessprotokoll.Editor!.IsKeyNotAvailable = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.IsEnabled);

        vm.CoMessprotokoll.Editor!.IsKeyAvailable = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.IsEnabled);

        Capture(window, "co-messung-schluesselnummer.png");
    }

    [AvaloniaFact]
    public void CoMessprotokoll_ImplausibleValue_ShowsWarningInEditor()
    {
        var (window, vm, session) = ShowWorkspace();

        session.AddCoBuilding("Mehrfamilienhaus A", 1, 1);
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");

        var cell = vm.CoMessprotokoll.MatrixRows[0].Cells[0];
        cell.OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        vm.CoMessprotokoll.Editor!.GasInputs[0].Value = 2500;
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.CoMessprotokoll.Editor!.IsCoImplausible);

        Capture(window, "co-messung-implausible-warning.png");
    }

    [AvaloniaFact]
    public void CoMessprotokoll_PerFloorApartmentCount_RendersIndependently()
    {
        var (window, vm, session) = ShowWorkspace();

        session.AddCoBuilding("Mehrfamilienhaus A", 2, 3);
        Dispatcher.UIThread.RunJobs();

        var building = session.Incident.Buildings[0];
        session.SetApartmentCount(building.Id, 0, 6);
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");

        var egRow = vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 0);
        var ogRow = vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 1);
        Assert.Equal(6, egRow.Cells.Count);
        Assert.Equal(3, ogRow.Cells.Count); // untouched floor keeps the building's default (#265)

        Capture(window, "co-messung-per-floor-count.png");
    }

    // #420: the Bezeichnung suggests the Stammdaten's Objekte, and a pick says what it brought.
    [AvaloniaFact]
    public void The_add_dialog_offers_the_objekte_and_shows_what_a_pick_brings()
    {
        var (window, vm, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");
        vm.CoMessprotokoll.AddBuildingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var nameBox = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "NewBuildingNameBox");
        var hint = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NewBuildingPremisesHint");
        Assert.Equal(new[] { "Wohnanlage Lindenallee", "Haus A" }, nameBox.ItemsSource!.Cast<string>());
        Assert.False(hint.IsVisible);

        nameBox.Text = "Wohnanlage Lindenallee";
        Dispatcher.UIThread.RunJobs();

        Assert.True(hint.IsVisible);
        Assert.Equal("Aus den Stammdaten · Lindenallee 12, 12345 Musterstadt · 4 Wohnungsbezeichnungen", hint.Text);
        Assert.Equal(4, vm.CoMessprotokoll.NewBuildingFloors);

        Capture(window, "co-messung-objekt-pick.png");
    }
}
