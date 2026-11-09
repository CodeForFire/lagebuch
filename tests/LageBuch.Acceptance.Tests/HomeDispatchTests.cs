using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The alarm from the alarm folder on the Home screen: what it shows, the Einsatzmittel to tick,
// and the folder card it comes from. Every value is fictional.
public sealed class HomeDispatchTests : IDisposable
{
    private const string AlarmXml = """
        <einsatz version="1">
        <test></test>
        <organisation><![CDATA[ Feuerwehr Musterstadt ]]></organisation>
        <kennung><einsatznummer>T 1.2 261108 00042</einsatznummer></kennung>
        <einsatzart><stichwort><![CDATA[ THL 2 T2917 ]]></stichwort><einsatztyp><![CDATA[ THL 2 VU eCall ]]></einsatztyp></einsatzart>
        <meldung><meldebild><![CDATA[ VU eCall - ohne Spracherwiderung ]]></meldebild><einsatzhinweis><![CDATA[ 2 PKW, 3 Betroffene ]]></einsatzhinweis></meldung>
        <zeiten><alarmierung iso="2026-11-08 17:17"/></zeiten>
        <einsatzort><ort><![CDATA[ 12345 Musterstadt Nord ]]></ort><strasse><![CDATA[ Hauptstraße 12 ]]></strasse>
        <koordinaten><wgs84 lat="48.1" lng="11.5"/></koordinaten></einsatzort>
        <einsatzmittel><gefordert><![CDATA[ Florian Musterstadt Wache 2<br>Florian Musterstadt 1<br>Florian Musterstadt 2/40/1<br>Florian Musterstadt 1/30/1 ]]></gefordert></einsatzmittel>
        </einsatz>
        """;

    private static readonly TimeZoneInfo Cet = TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "CET", "CET");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 17, 42, 0, TimeSpan.FromHours(1));

    private readonly string _folder = Directory.CreateTempSubdirectory("dispatch-ui-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class Md : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Vehicles =
            [
                new Vehicle("Wache 2", "Florian Musterstadt 2/40/1", 9, VehicleType: "HLF 20"),
                new Vehicle("Wache 1", "Florian Musterstadt 1/30/1", 3, VehicleType: "DLK 23/12"),
            ],
        };

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class Inbox : IDispatchInboxStore
    {
        public string? Folder { get; set; }

        public HashSet<string> Handled { get; } = new(StringComparer.Ordinal);

        public string? GetFolder() => Folder;

        public void SetFolder(string? folder) => Folder = folder;

        public IReadOnlySet<string> GetHandled() => Handled;

        public void MarkHandled(string key, DateTimeOffset at) => Handled.Add(key);
    }

    private void DropAlarm(string xml = AlarmXml)
    {
        var path = Path.Join(_folder, "alarm.xml");
        File.WriteAllText(path, xml);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-20).UtcDateTime);
    }

    private static (Window Window, HomeViewModel Vm) ShowHome(string? folder, double width = 1100, string? renderTo = null, bool wired = true)
    {
        var clock = new FixedClock { Now = Now };
        var dialogs = new FakeDialogs();
        var dispatch = wired ? new DispatchInboxViewModel(new Inbox { Folder = folder }, new Md(), dialogs, clock, new ManualTicker(), zone: Cet) : null;
        var vm = new HomeViewModel(new FakeStore(), new Md(), new NoRecent(), dialogs, clock, new ManualTicker(), new NoopAlarmService(), new NoopIncidentHostController(), "1.0.0", dispatch: dispatch);

        // Hosted in a "Shell" container like MainView's root, so HomeView's phone query applies.
        var shell = new Panel { Children = { new HomeView { DataContext = vm } } };
        Container.SetName(shell, "Shell");
        Container.SetSizing(shell, ContainerSizing.Width);
        var window = new Window { Content = shell, Width = width, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Opt-in PNG capture for PR screenshots; skipped in a normal test run.
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (renderTo is not null && !string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Join(dir, renderTo));
        }

        return (window, vm);
    }

    private static T ByName<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void An_alarm_in_the_folder_is_offered_with_its_stichwort_address_and_units()
    {
        DropAlarm();
        foreach (var (width, png) in new[] { (1100.0, "home-dispatch-offer.png"), (412.0, "home-dispatch-offer-phone.png") })
        {
            var (window, _) = ShowHome(_folder, width, png);

            Assert.True(ByName<Border>(window, "DispatchOfferBanner").IsEffectivelyVisible);
            Assert.Equal("THL 2 T2917", ByName<TextBlock>(window, "DispatchOfferTitle").Text);
            Assert.StartsWith("Hauptstraße 12, 12345 Musterstadt Nord · alarmiert 17:17", ByName<TextBlock>(window, "DispatchOfferSummary").Text, StringComparison.Ordinal);
            var boxes = ByName<ItemsControl>(window, "DispatchUnitList").GetVisualDescendants().OfType<CheckBox>().ToList();
            Assert.Equal([false, false, true, true], boxes.Select(b => b.IsChecked == true));
            Assert.Equal("Florian Musterstadt Wache 2", AutomationProperties.GetName(boxes[0]));
            Assert.False(ByName<Border>(window, "DispatchProbeTag").IsVisible);

            // Every folder action stays inside the card, at phone width too.
            var card = ByName<Border>(window, "DispatchFolderCard");
            foreach (var button in ByName<WrapPanel>(window, "DispatchFolderActions").Children.OfType<Button>())
            {
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), card)?.X;
                Assert.True(right <= card.Bounds.Width, $"{button.Name} ends at {right}, past the card's {card.Bounds.Width}");
            }

            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_probealarm_carries_its_tag()
    {
        DropAlarm(AlarmXml.Replace("<test></test>", "<test>1</test>", StringComparison.Ordinal));

        var (window, _) = ShowHome(_folder, renderTo: "home-dispatch-probealarm.png");

        Assert.True(ByName<Border>(window, "DispatchProbeTag").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Verwerfen_takes_the_offer_off_the_screen()
    {
        DropAlarm();
        var (window, _) = ShowHome(_folder);

        ByName<Button>(window, "DismissDispatchButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ByName<Panel>(window, "DispatchOfferPanel").IsVisible);
    }

    [AvaloniaFact]
    public void Without_a_folder_the_card_offers_to_choose_one_and_nothing_is_on_offer()
    {
        var (window, _) = ShowHome(null, renderTo: "home-dispatch-folder-unset.png");

        Assert.False(ByName<Panel>(window, "DispatchOfferPanel").IsVisible);
        Assert.True(ByName<TextBlock>(window, "DispatchFolderHint").IsEffectivelyVisible);
        Assert.True(ByName<Button>(window, "OpenAlarmFileButton").IsEffectivelyVisible);
        Assert.False(ByName<Button>(window, "ClearDispatchFolderButton").IsVisible);
    }

    [AvaloniaFact]
    public void A_set_folder_is_named_on_the_card()
    {
        var (window, _) = ShowHome(_folder);

        Assert.Equal($"Alarm-Mails aus {_folder}", ByName<TextBlock>(window, "DispatchFolderText").Text);
        Assert.True(ByName<Button>(window, "ClearDispatchFolderButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Where_alarm_import_is_not_wired_the_home_screen_shows_none_of_it()
    {
        var (window, _) = ShowHome(null, renderTo: "home-dispatch-not-wired.png", wired: false);

        Assert.False(ByName<Panel>(window, "DispatchOfferPanel").IsVisible);
        Assert.False(ByName<Panel>(window, "DispatchFolderPanel").IsVisible);
    }

    private sealed class NoRecent : IRecentFilesStore
    {
        public IReadOnlyList<string> GetRecent() => [];

        public void Add(string path)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
