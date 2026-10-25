using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The AUSGABE tab: the Ausgabeliste. Doubles as the PR screenshot capture (RENDER_OUT), same idiom
// as InvolvedPartiesTabRenderTests. Fictional people only.
public class IssuedItemsTabRenderTests
{
    private static readonly Device[] Devices =
    [
        new("Mobilteil 1", 1), new("Mobilteil 2", 1), new("Digitalkamera", 1), new("HRT", 2),
        new("Weste EAL", 2), new("Weste Presse", 1), new("AED", 1), new("Fernglas", 1),
    ];

    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        double width = 1920, double height = 1032, bool withItems = true)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        if (withItems)
        {
            session.IssueItem("HRT", "Beispiel, Erika", "0171 0000001", "THW OV Musterstadt, Zugführerin", "Kanal 2, Akku voll");
            session.IssueItem("Weste Presse", "Mustermann, Max", "0171 0000002", "Musterstädter Tagblatt", null);
            session.IssueItem("Mobilteil 1", "PHK Musterfrau", null, "Polizei, Streife 12/3", null);
            session.ReturnIssuedItem(session.Incident.IssuedItems[2].Id);
        }

        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData() with { Devices = Devices },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
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

    private static T Named<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First(v => v.Name == name);

    // Last, after KONTAKTE: Ctrl+1…9 open the rail by position, so a new module must not move the
    // ones crews already reach by number.
    [AvaloniaFact]
    public void The_rail_carries_an_ausgabe_tab_after_kontakte()
    {
        var (window, _, _) = ShowWorkspace();
        var headers = WorkspaceRenderHelper.RailHeaders(window).ToList();

        Assert.Equal(headers.IndexOf("KONTAKTE") + 1, headers.IndexOf("AUSGABE"));
    }

    [AvaloniaFact]
    public void The_ausgabe_tab_lists_what_is_still_out_first_and_counts_it()
    {
        var (window, _, _) = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        var list = Named<ItemsControl>(content, "IssuedItemsList");
        Assert.Equal(3, list.ItemCount);
        Assert.Equal(["HRT", "Weste Presse", "Mobilteil 1"], list.Items.Cast<IssuedItemRow>().Select(r => r.Device));
        Assert.Equal("2 Geräte ausgegeben", Named<TextBlock>(content, "SummaryText").Text);
        Assert.Equal(2, content.GetVisualDescendants().OfType<Button>().Count(b => b.Name == "ReturnButton" && b.IsEffectivelyVisible));
        Assert.True(Named<Button>(content, "IssueItemButton").IsEffectivelyVisible);
        Assert.False(Named<TextBlock>(content, "EmptyText").IsEffectivelyVisible);
        Capture(window, "ausgabe-tab.png");
    }

    // The one Weste Presse is already out, so the hint under GERÄT turns amber; the HRT has one left.
    [AvaloniaFact]
    public void The_stock_hint_under_geraet_turns_amber_when_none_is_left()
    {
        var (window, vm, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var hint = Named<TextBlock>(content, "DeviceStockText");

        Assert.False(hint.IsEffectivelyVisible);

        vm.IssuedItems.NewDevice = "HRT";
        Dispatcher.UIThread.RunJobs();
        Assert.True(hint.IsEffectivelyVisible);
        Assert.Equal("1 von 2 frei", hint.Text);
        Assert.DoesNotContain("out", hint.Classes);

        vm.IssuedItems.NewDevice = "Weste Presse";
        vm.IssuedItems.NewIssuedTo = "Musterfrau, Erika";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Kein Gerät frei: 1 von 1 ausgegeben", hint.Text);
        Assert.Contains("out", hint.Classes);
        Capture(window, "ausgabe-stock-hint.png");
    }

    [AvaloniaFact]
    public void An_empty_ausgabe_tab_says_so()
    {
        var (window, _, _) = ShowWorkspace(withItems: false);

        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        Assert.True(Named<TextBlock>(content, "EmptyText").IsEffectivelyVisible);
        Assert.False(Named<TextBlock>(content, "SummaryText").IsEffectivelyVisible);
        Capture(window, "ausgabe-tab-empty.png");
    }

    [AvaloniaFact]
    public void Zurueck_on_a_row_records_the_return()
    {
        var (window, _, session) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        content.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ReturnButton" && b.IsEffectivelyVisible)
            .Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, session.Incident.ItemsOut);
        Assert.Equal("1 Gerät ausgegeben", Named<TextBlock>(content, "SummaryText").Text);
    }

    [AvaloniaFact]
    public void Issuing_through_the_form_puts_the_geraet_in_the_list()
    {
        var (window, vm, session) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        vm.IssuedItems.NewDevice = "AED";
        vm.IssuedItems.NewIssuedTo = "Beispiel, Lena";
        Named<Button>(content, "IssueItemButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, session.Incident.IssuedItems.Count);
        Assert.Equal(4, Named<ItemsControl>(content, "IssuedItemsList").ItemCount);
    }

    // The GERÄT suggestions are a popup, a TopLevel of its own that the window's frame leaves out,
    // so the capture draws the popup's frame over the window's at its offset (as #728 does).
    [AvaloniaFact]
    public void Opening_geraet_offers_the_stammdaten_geraete()
    {
        var (window, _, _) = ShowWorkspace(withItems: false);
        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var box = Named<AutoCompleteBox>(content, "NewDeviceBox");

        box.Focus();
        box.Text = string.Empty;
        box.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        var popup = box.GetVisualDescendants().OfType<Popup>().Single();
        var list = popup.Child!.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.True(popup.IsOpen);
        Assert.Equal(Devices.Select(d => d.Name), list.Items.Cast<string>());

        CaptureWithPopup(window, popup, "ausgabe-geraete-suggestions.png");
    }

    private static void CaptureWithPopup(Window window, Popup popup, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        var popupRoot = TopLevel.GetTopLevel(popup.Child)!;
        Dispatcher.UIThread.RunJobs();
        using var windowFrame = window.CaptureRenderedFrame()!;
        using var popupFrame = popupRoot.CaptureRenderedFrame()!;
        var offset = popupRoot.PointToScreen(default) - window.PointToScreen(default);
        using var composite = new RenderTargetBitmap(windowFrame.PixelSize);
        using (var context = composite.CreateDrawingContext())
        {
            context.DrawImage(windowFrame, new Rect(windowFrame.Size));
            context.DrawImage(popupFrame, new Rect(new Point(offset.X, offset.Y), popupFrame.Size));
        }

        Directory.CreateDirectory(dir);
        composite.Save(Path.Join(dir, name), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void The_ausgabe_tab_renders_on_a_phone()
    {
        var (window, vm, _) = ShowWorkspace(412, 915);
        WorkspaceRenderHelper.SelectTab(window, "AUSGABE");

        Assert.True(vm.IssuedItems.IsNarrow);
        Assert.False(vm.IssuedItems.ShowComposer);
        Capture(window, "ausgabe-tab-phone.png");
    }

    [AvaloniaFact]
    public void Closing_with_a_geraet_still_out_warns_in_the_dialog()
    {
        var (window, vm, _) = ShowWorkspace();

        vm.CloseIncidentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("2 Gerät(e) noch nicht zurückgegeben", vm.PendingConfirm!.Message, StringComparison.Ordinal);
        Capture(window, "ausgabe-close-warning.png");
    }
}
