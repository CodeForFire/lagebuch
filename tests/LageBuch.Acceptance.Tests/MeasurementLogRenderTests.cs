using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.Acceptance.Tests;

// The MESSPROTOKOLL tab of the MESSUNG entry (#930): the Messreihe, its Bewertung and Korrektur,
// the Messpunkte with the weather, the Messgrößen and the Geräte, on a desktop and on a phone.
// Doubles as the PR screenshot capture (RENDER_OUT). Fictional Einsatz, fictional crews.
public class MeasurementLogRenderTests
{
    internal static readonly GasDefinition[] Gases =
    [
        GasDefinition.DefaultCo,
        new("O2", "Vol-%", GasDirection.Falling, 19.5m, 17m, 10m, 25m),
        new("Ex", "%UEG", GasDirection.Rising, 10m, 20m, 40m, 100m),
        new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m),
    ];

    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    private static DateTimeOffset At(int hour, int minute) => new(2026, 6, 22, hour, minute, 0, Offset);

    /// <summary>
    /// A small ABC-Einsatz: two Messgeräte, three Messpunkte, six Messungen with every band, one
    /// confirmed, one Ausreißer and one corrected value, and two weather readings.
    /// </summary>
    internal static void Seed(LocalIncidentSession session)
    {
        session.AddMeasuringDevice(DeviceKind.MultiGas, "MGM 1", "GW-G/MG-01", "GW-G Musterstadt", "Bump-Test 08:40");
        session.AddMeasuringDevice(DeviceKind.Pid, "PID 1", "ELW/PID-01", "ELW Musterstadt", null);
        session.AddMeasuringPoint("Kellerabgang", "Treppenhaus Nord, UG", MeasurementZone.Danger, Gases);
        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Transition, Gases);
        session.AddMeasuringPoint("Absperrgrenze Ost", "Ecke Industriestraße", MeasurementZone.Cordon, Gases);

        var incident = session.Incident;
        var mgm = incident.MeasuringDevices[0].Id;
        foreach (var channel in incident.MeasurementChannels.ToList())
        {
            session.UpdateMeasurementChannel(channel.Id, mgm, true, null);
        }

        var channels = incident.MeasurementChannels;
        var (co, o2, ex, h2s) = (channels[0].Id, channels[1].Id, channels[2].Id, channels[3].Id);
        var points = incident.MeasuringPoints;
        var (keller, hof, ost) = (points[0].Id, points[1].Id, points[2].Id);
        static MeasuredValue V(decimal value) => MeasuredValue.Single(value);
        var angriff = ("Florian Musterstadt 40/1 · Angriffstrupp", ProtectiveEquipment.ScbaForm1, MeasurementMode.Remote);
        var messtrupp = ("Florian Musterstadt 40/2 · Messtrupp", ProtectiveEquipment.FilterForm1, MeasurementMode.Remote);
        var elw = ("ELW Musterstadt", ProtectiveEquipment.None, MeasurementMode.FromVehicle);
        void Record(
            DateTimeOffset at,
            string order,
            Guid point,
            (string Team, ProtectiveEquipment Protection, MeasurementMode Mode) crew,
            (bool Odour, bool Deposits, string? Note) observed,
            Dictionary<Guid, MeasuredValue> values) =>
            session.RecordMeasurement(
                at, order, point, crew.Team, crew.Protection, crew.Mode, observed.Odour, observed.Deposits, observed.Note, values);

        (bool Odour, bool Deposits, string? Note) nothing = (false, false, null);
        Record(At(8, 20), "MA 01", keller, angriff, nothing, new() { [co] = V(140m), [o2] = V(20.9m), [ex] = V(0m), [h2s] = V(0m) });
        Record(At(8, 32), "MA 01", keller, angriff, (true, false, "stechender Geruch am Kellerabgang"), new() { [co] = V(38m), [o2] = V(20.1m), [ex] = new(8m, 14m), [h2s] = V(2m) });
        Record(At(8, 40), "MA 02", hof, messtrupp, nothing, new() { [co] = V(6m), [o2] = V(20.9m), [ex] = V(0m), [h2s] = V(0m) });
        Record(At(8, 48), "MA 03", ost, elw, nothing, new() { [co] = V(64m) });
        Record(At(8, 52), "MA 03", ost, elw, nothing, new() { [co] = V(3m), [o2] = V(20.9m) });
        Record(At(8, 55), "MA 04", keller, angriff, (false, true, null), new() { [co] = V(210m), [o2] = V(18.2m), [ex] = V(22m), [h2s] = V(12m) });

        var measurements = incident.Measurements;
        session.CorrectMeasurementValue(measurements[0].Id, co, V(14m), "Ablesefehler");
        session.AssessMeasurement(measurements[2].Id, MeasurementStatus.Confirmed, "Fachberater ABC", "Hofeinfahrt frei, Übergangsbereich bleibt");
        session.AssessMeasurement(measurements[3].Id, MeasurementStatus.Outlier, "Fachberater ABC", "Gerät nicht genullt, Folgemessung 3 ppm");

        session.AddWeatherObservation(At(8, 15), WindDirection.W, 12m, 14m, CloudCover.Clear, Precipitation.None, "Handwindmesser");
        session.AddWeatherObservation(At(8, 45), WindDirection.SW, 18m, 15m, CloudCover.UpToHalf, Precipitation.None, "Wetterstation Fahrzeug");
    }

    internal static LocalIncidentSession NewSession(bool seeded = true, bool closed = false)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        if (seeded)
        {
            Seed(session);
        }

        if (closed)
        {
            session.Close();
        }

        return session;
    }

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(
        double width = 1920, double height = 1032, bool seeded = true, bool closed = false)
    {
        var session = NewSession(seeded, closed);
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData() with { Gases = Gases },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static MeasurementLogView OpenLog(Window window)
    {
        WorkspaceRenderHelper.SelectModuleTab(window, "MESSUNG", "MESSPROTOKOLL");
        Dispatcher.UIThread.RunJobs();
        return WorkspaceRenderHelper.SelectedTabContent(window).GetVisualDescendants().OfType<MeasurementLogView>().Single();
    }

    private static void ShowPage(MeasurementLogViewModel log, MeasurementLogPage page)
    {
        log.SelectPageCommand.Execute(page);
        Dispatcher.UIThread.RunJobs();
    }

    private static void OpenComposer(MeasurementLogView view)
    {
        Named<Button>(view, "OpenComposerButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
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

    private static T Named<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First(v => v.Name == name);

    private static IEnumerable<T> AllNamed<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().Where(v => v.Name == name);

    [AvaloniaFact]
    public void The_messung_entry_carries_a_gebaeude_and_a_messprotokoll_tab()
    {
        var (window, _) = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "MESSUNG");
        var strip = WorkspaceRenderHelper.ModuleTabStrip(window);

        Assert.Equal(["GEBÄUDE", "MESSPROTOKOLL"], strip.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Equal(MeasurementTabsViewModel.GebaeudeTab, strip.SelectedIndex);
        Assert.Single(WorkspaceRenderHelper.SelectedTabContent(window).GetVisualDescendants().OfType<CoMessprotokollView>());
        Capture(window, "messung-gebaeude.png");
    }

    [AvaloniaFact]
    public void The_messreihe_shows_a_tile_per_messgroesse_and_the_messungen_newest_first()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        Assert.True(Named<DockPanel>(view, "MessreihePage").IsEffectivelyVisible);
        Assert.Equal(4, Named<ItemsControl>(view, "TileList").ItemCount);
        Assert.Equal(["Alle", "Kellerabgang", "Hofeinfahrt", "Absperrgrenze Ost"], vm.MeasurementLog.PointFilterOptions.Select(o => o.Label));
        Assert.Equal(4, Named<ListBox>(view, "PointFilter").ItemCount);

        var list = Named<ItemsControl>(view, "MeasurementList");
        Assert.Equal(6, list.ItemCount);
        Assert.Equal("08:55", list.Items.Cast<MeasurementRow>().First().TimeText);
        Assert.True(Named<Border>(view, "MeasurementTablePanel").IsEffectivelyVisible);
        Assert.False(Named<Border>(view, "MeasurementCardPanel").IsEffectivelyVisible);
        Assert.False(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.True(Named<Button>(view, "OpenComposerButton").IsEffectivelyVisible);
        Assert.Equal(HorizontalAlignment.Left, Named<Button>(view, "OpenComposerButton").HorizontalAlignment);
        Capture(window, "messprotokoll-messreihe.png");

        OpenComposer(view);
        Assert.True(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.True(Named<Button>(view, "RecordMeasurementButton").IsEffectivelyVisible);
        Assert.True(Named<Button>(view, "CloseComposerButton").IsEffectivelyVisible);
        Assert.False(Named<Button>(view, "OpenComposerButton").IsEffectivelyVisible);
        Capture(window, "messprotokoll-neue-messung.png");
    }

    // The mockup's Verlauf: every tile draws its values small, and a tap opens them large, one
    // line per Messpunkt with the bands dashed across.
    [AvaloniaFact]
    public void A_tile_shows_its_verlauf_small_and_a_tap_opens_it_large()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);
        var coTile = Named<ItemsControl>(view, "TileList").GetRealizedContainers().First();

        var spark = Named<MeasurementTrendChart>(coTile, "TileSpark");
        Assert.True(spark.IsEffectivelyVisible);
        Assert.Equal(3, spark.Trend?.Series.Count);
        Assert.False(Named<Border>(view, "TrendPanel").IsEffectivelyVisible);

        Named<Button>(coTile, "TileButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Border>(view, "TrendPanel").IsEffectivelyVisible);
        Assert.Equal("VERLAUF · CO (ppm)", Named<TextBlock>(view, "TrendHeading").Text);
        Assert.Same(vm.MeasurementLog.SelectedTrend, Named<MeasurementTrendChart>(view, "TrendChart").Trend);
        Assert.True(Named<MeasurementTrendChart>(view, "TrendChart").Bounds.Height >= 150);
        Capture(window, "messprotokoll-verlauf.png");

        Named<Button>(view, "CloseTrendButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Named<Border>(view, "TrendPanel").IsEffectivelyVisible);
    }

    // The four-row form, docked open for good, took every pixel below the Messpunkt chips on a
    // laptop and left the Messreihe 0 px tall. Closed until asked for, the table keeps the room.
    [AvaloniaFact]
    public void On_a_laptop_the_messreihe_keeps_its_room_below_the_tiles()
    {
        var (window, _) = ShowWorkspace(width: 1366, height: 768);
        var view = OpenLog(window);
        window.UpdateLayout();

        Assert.True(
            Named<Border>(view, "MeasurementTablePanel").Bounds.Height >= 200,
            $"The Messreihe is {Named<Border>(view, "MeasurementTablePanel").Bounds.Height} px tall.");
        Capture(window, "messprotokoll-laptop.png");
    }

    // The band is written out on the tile and under the value, never left to the colour.
    [AvaloniaFact]
    public void A_lethal_value_says_lebensgefaehrlich_on_its_tile_and_in_its_cell()
    {
        var (window, _) = ShowWorkspace();
        var view = OpenLog(window);

        var coTile = Named<ItemsControl>(view, "TileList").GetRealizedContainers().First();
        var severity = Named<TextBlock>(coTile, "TileSeverity");
        Assert.Equal("Lebensgefährlich", severity.Text);
        Assert.Contains("lethal", severity.Classes);

        var newest = Named<ItemsControl>(view, "MeasurementList").GetRealizedContainers().First();
        Assert.Contains(AllNamed<TextBlock>(newest, "CellSeverity"), t => t.IsEffectivelyVisible && t.Text == "Lebensgefährlich");
    }

    [AvaloniaFact]
    public void An_ausreisser_stays_in_the_list_struck_through_and_says_so()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        var list = Named<ItemsControl>(view, "MeasurementList");
        var index = vm.MeasurementLog.Rows.ToList().FindIndex(r => r.IsOutlier);
        var row = list.ContainerFromIndex(index)!;

        Assert.Equal("Ausreißer", Named<TextBlock>(row, "RowStatus").Text);
        Assert.Contains("outlier", Named<Border>(row, "MeasurementRowBorder").Classes);
        Assert.NotNull(Named<TextBlock>(row, "CellValue").TextDecorations);

        var confirmed = list.ContainerFromIndex(vm.MeasurementLog.Rows.ToList().FindIndex(r => r.IsConfirmed))!;
        Assert.Equal("bestätigt", Named<TextBlock>(confirmed, "RowStatus").Text);
        Assert.Null(Named<TextBlock>(confirmed, "CellValue").TextDecorations);
    }

    [AvaloniaFact]
    public void A_corrected_value_is_marked_korrigiert()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        var list = Named<ItemsControl>(view, "MeasurementList");
        var oldest = list.ContainerFromIndex(vm.MeasurementLog.Rows.Count - 1)!;
        var marks = AllNamed<TextBlock>(oldest, "CorrectedMark").Where(t => t.IsEffectivelyVisible).ToList();

        Assert.Single(marks);
        Assert.Contains(oldest.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } s && s.StartsWith("CO korrigiert: 140 → 14", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Picking_a_messpunkt_chip_narrows_the_messreihe()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        Named<ListBox>(view, "PointFilter").SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Hofeinfahrt", vm.MeasurementLog.SelectedPointFilter?.Label);
        Assert.Equal(1, Named<ItemsControl>(view, "MeasurementList").ItemCount);
        Assert.Equal("Hofeinfahrt", vm.MeasurementLog.NewPointName);
    }

    // BEWERTEN opens the Bewertung where the form was, so the thumb is already there.
    [AvaloniaFact]
    public void Bewerten_opens_the_bewertung_in_place_of_the_form_and_saving_records_it()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);
        var log = vm.MeasurementLog;
        OpenComposer(view);
        var newest = Named<ItemsControl>(view, "MeasurementList").GetRealizedContainers().First();

        Named<Button>(newest, "AssessButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Border>(view, "AssessmentPanel").IsEffectivelyVisible);
        Assert.False(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.Equal("08:55 · MA 04 · Kellerabgang #3 · Florian Musterstadt 40/1 · Angriffstrupp", Named<TextBlock>(view, "AssessmentHeading").Text);
        log.AssessmentText = "Kellerabgang räumen, Lüfter ansetzen";
        Dispatcher.UIThread.RunJobs();
        Capture(window, "messprotokoll-bewertung.png");

        log.ConfirmAssessmentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named<Border>(view, "AssessmentPanel").IsEffectivelyVisible);
        Assert.True(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.Equal("bestätigt", log.Rows[0].StatusText);
    }

    [AvaloniaFact]
    public void Tapping_a_value_opens_its_korrektur()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);
        var newest = Named<ItemsControl>(view, "MeasurementList").GetRealizedContainers().First();
        var cell = newest.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("cell"));

        cell.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Border>(view, "CorrectionPanel").IsEffectivelyVisible);
        Assert.False(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.Equal("CO (ppm) · bisher: 210", Named<TextBlock>(view, "CorrectionHeadingText").Text);
        vm.MeasurementLog.CorrectionValue = "21";
        vm.MeasurementLog.CorrectionReason = "Komma vergessen";
        Dispatcher.UIThread.RunJobs();
        Capture(window, "messprotokoll-korrektur.png");
    }

    [AvaloniaFact]
    public void The_messpunkte_and_wetter_page_lists_both()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        ShowPage(vm.MeasurementLog, MeasurementLogPage.MesspunkteWetter);

        Assert.False(Named<DockPanel>(view, "MessreihePage").IsEffectivelyVisible);
        Assert.Equal(3, Named<ItemsControl>(view, "PointList").ItemCount);
        Assert.Equal(2, Named<ItemsControl>(view, "WeatherList").ItemCount);
        Assert.True(Named<Border>(view, "PointForm").IsEffectivelyVisible);
        Assert.True(Named<Border>(view, "WeatherForm").IsEffectivelyVisible);
        Assert.Contains("chipon", Named<Button>(view, "MesspunkteWetterPageButton").Classes);
        Assert.DoesNotContain("chipon", Named<Button>(view, "MessreihePageButton").Classes);
        Capture(window, "messprotokoll-messpunkte-wetter.png");
    }

    [AvaloniaFact]
    public void The_messgroessen_page_lists_each_messgroesse_and_keeps_one_with_readings()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        ShowPage(vm.MeasurementLog, MeasurementLogPage.Messgroessen);

        var list = Named<ItemsControl>(view, "ChannelList");
        Assert.Equal(4, list.ItemCount);
        var remove = Named<Button>(list.ContainerFromIndex(0)!, "RemoveChannelButton");
        Assert.False(remove.IsEffectivelyEnabled);
        Assert.True(Named<Border>(view, "ChannelForm").IsEffectivelyVisible);
        Capture(window, "messprotokoll-messgroessen.png");
    }

    [AvaloniaFact]
    public void The_geraete_page_lists_the_messgeraete()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);

        ShowPage(vm.MeasurementLog, MeasurementLogPage.Geraete);

        Assert.Equal(2, Named<ItemsControl>(view, "DeviceList").ItemCount);
        Assert.True(Named<Border>(view, "DeviceForm").IsEffectivelyVisible);
        Capture(window, "messprotokoll-geraete.png");
    }

    // 412dp cannot carry a table of ten Messgrößen: the phone reads one card per Messung.
    [AvaloniaFact]
    public void On_a_phone_the_messreihe_is_cards_and_nothing_runs_off_the_edge()
    {
        var (window, vm) = ShowWorkspace(412, 915);
        var view = OpenLog(window);

        Assert.True(vm.MeasurementLog.IsNarrow);
        Assert.True(Named<Border>(view, "MeasurementCardPanel").IsEffectivelyVisible);
        Assert.False(Named<Border>(view, "MeasurementTablePanel").IsEffectivelyVisible);
        Assert.Equal(6, Named<ItemsControl>(view, "MeasurementCards").ItemCount);
        Assert.False(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.True(Named<Button>(view, "OpenComposerButton").IsEffectivelyVisible);
        Assert.Equal(HorizontalAlignment.Stretch, Named<Button>(view, "OpenComposerButton").HorizontalAlignment);
        AssertWithinPhone(window, Named<Border>(view, "MeasurementCardPanel"));
        Capture(window, "messprotokoll-messreihe-phone.png");
    }

    // The form opens from the bottom button, stacks its fields at a thumb-sized width and scrolls.
    [AvaloniaFact]
    public void On_a_phone_the_entry_form_opens_from_its_button_and_stacks()
    {
        var (window, _) = ShowWorkspace(412, 915);
        var view = OpenLog(window);

        Named<Button>(view, "OpenComposerButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.False(Named<Button>(view, "OpenComposerButton").IsEffectivelyVisible);
        Assert.True(Named<Button>(view, "CloseComposerButton").IsEffectivelyVisible);
        Assert.Equal(Avalonia.Layout.Orientation.Vertical, Named<WrapPanel>(view, "EntryFields").Orientation);
        AssertWithinPhone(window, Named<Border>(view, "InputDock"));
        AssertWithinPhone(window, Named<TextBox>(view, "NewNoteBox"));
        Capture(window, "messprotokoll-form-phone.png");
    }

    private static void AssertWithinPhone(Window window, Control control)
    {
        var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X;
        Assert.True(right <= 413.0, $"{control.Name} ends at {right}");
    }

    // A closed Einsatz is a historical record: every form and every BEWERTEN goes.
    [AvaloniaFact]
    public void A_closed_einsatz_shows_the_messreihe_without_any_form()
    {
        var (window, vm) = ShowWorkspace(closed: true);
        var view = OpenLog(window);

        Assert.Equal(6, Named<ItemsControl>(view, "MeasurementList").ItemCount);
        Assert.False(Named<Border>(view, "InputDock").IsEffectivelyVisible);
        Assert.DoesNotContain(AllNamed<Button>(view, "AssessButton"), b => b.IsEffectivelyVisible);
        Capture(window, "messprotokoll-geschlossen.png");

        var forms = new (MeasurementLogPage Page, string[] Names)[]
        {
            (MeasurementLogPage.MesspunkteWetter, ["PointForm", "WeatherForm"]),
            (MeasurementLogPage.Messgroessen, ["ChannelForm"]),
            (MeasurementLogPage.Geraete, ["DeviceForm"]),
        };
        foreach (var (page, names) in forms)
        {
            ShowPage(vm.MeasurementLog, page);
            Assert.All(names, n => Assert.False(Named<Border>(view, n).IsEffectivelyVisible, n));
        }

        Assert.DoesNotContain(AllNamed<Button>(view, "EditPointButton"), b => b.IsEffectivelyVisible);
        Assert.DoesNotContain(AllNamed<Button>(view, "EditDeviceButton"), b => b.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_fresh_messprotokoll_says_it_is_empty_and_offers_the_stammdaten_messgase()
    {
        var (window, _) = ShowWorkspace(seeded: false);
        var view = OpenLog(window);
        OpenComposer(view);

        Assert.Equal("Noch keine Messung.", Named<TextBlock>(view, "MeasurementEmptyText").Text);
        Assert.True(Named<TextBlock>(view, "MeasurementEmptyText").IsEffectivelyVisible);
        Assert.Equal(4, Named<ItemsControl>(view, "ValueInputList").ItemCount);
    }

    // A value that is not a reading is named under its own box, not only in the summary.
    [AvaloniaFact]
    public void A_value_that_is_not_a_reading_is_named_under_its_box()
    {
        var (window, vm) = ShowWorkspace();
        var view = OpenLog(window);
        var log = vm.MeasurementLog;
        OpenComposer(view);

        log.NewPointName = "Kellerabgang";
        log.ValueInputs[0].Text = "viel";
        log.RecordMeasurementCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var field = Named<ItemsControl>(view, "ValueInputList").ContainerFromIndex(0)!
            .GetVisualDescendants().OfType<HeaderedContentControl>().First();
        Assert.Contains("invalid", field.Classes);
        Assert.Contains(field.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "FieldError" && t.IsEffectivelyVisible && t.Text == MeasurementLogViewModel.ValueFormatError);
        Assert.True(Named<TextBlock>(view, "EntryErrorText").IsEffectivelyVisible);
    }
}
