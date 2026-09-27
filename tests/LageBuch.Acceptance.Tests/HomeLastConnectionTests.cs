using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The Home card naming where this device was last joined to (#464), and its way back in.
public class HomeLastConnectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 14, 5, 0, TimeSpan.FromHours(2));

    private sealed class Md : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty;

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class NoRecent : IRecentFilesStore
    {
        public IReadOnlyList<string> GetRecent() => Array.Empty<string>();

        public void Add(string path)
        {
        }
    }

    private sealed class Last : ILastConnectionStore
    {
        private readonly LastConnection? _last;

        public Last(LastConnection? last) => _last = last;

        public LastConnection? GetLast() => _last;

        public void SetLast(LastConnection connection)
        {
        }

        public void Clear()
        {
        }
    }

    private static (Window Window, HomeViewModel Vm) ShowHome(LastConnection? last, double width = 1100)
    {
        var vm = new HomeViewModel(
            new FakeStore(),
            new Md(),
            new NoRecent(),
            new FakeDialogs(),
            new FixedClock(),
            new ManualTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0",
            lastConnection: new Last(last));

        var window = new Window { Content = new HomeView { DataContext = vm }, Width = width, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static T ByName<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void The_card_names_host_stichwort_and_time()
    {
        var (window, _) = ShowHome(new LastConnection("elw-1", "B3 Wohnung", T0));

        Assert.True(ByName<Border>(window, "LastConnectionCard").IsEffectivelyVisible);
        Assert.Equal("elw-1", ByName<TextBlock>(window, "LastConnectionHost").Text);
        Assert.Equal("B3 Wohnung · 24.09.2026 14:05", ByName<TextBlock>(window, "LastConnectionDetail").Text);
    }

    [AvaloniaFact]
    public void The_card_is_hidden_when_this_device_never_joined()
    {
        var (window, _) = ShowHome(null);

        Assert.False(ByName<Border>(window, "LastConnectionCard").IsVisible);
    }

    [AvaloniaFact]
    public void Neu_verbinden_asks_for_the_join_dialog()
    {
        var (window, vm) = ShowHome(new LastConnection("elw-1", null, T0));
        var requested = false;
        vm.ReconnectRequested = () => requested = true;

        ByName<Button>(window, "ReconnectButton").Command!.Execute(null);

        Assert.True(requested);
    }

    [AvaloniaFact]
    public void The_forget_button_is_named_and_hides_the_card()
    {
        var (window, _) = ShowHome(new LastConnection("elw-1", null, T0, "5393"));
        var forget = ByName<Button>(window, "ForgetLastConnectionButton");

        Assert.Equal("Letzte Verbindung vergessen", AutomationProperties.GetName(forget));

        forget.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ByName<Border>(window, "LastConnectionCard").IsVisible);
    }

    // On a phone a long host and Stichwort wrap; both buttons must stay on the card.
    [AvaloniaFact]
    public void On_a_phone_the_buttons_stay_inside_the_card()
    {
        var (window, _) = ShowHome(
            new LastConnection("elw-1.tail1234.ts.net:5859", "TH Person eingeklemmt nach Verkehrsunfall", T0),
            width: 360);

        var card = ByName<Border>(window, "LastConnectionCard");
        foreach (var name in new[] { "ReconnectButton", "ForgetLastConnectionButton" })
        {
            var button = ByName<Button>(window, name);
            var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), card)!.Value.X;

            Assert.True(right <= card.Bounds.Width, $"{name} ends at x={right} on a {card.Bounds.Width}px card");
        }

        // The buttons take a line of their own rather than squeezing the text into a sliver.
        Assert.True(ByName<LeadTrailPanel>(window, "LastConnectionLine").IsWrapped);
    }

    [AvaloniaFact]
    public void On_a_desktop_text_and_buttons_share_one_line()
    {
        var (window, _) = ShowHome(new LastConnection("elw-1", "B3 Wohnung", T0));

        Assert.False(ByName<LeadTrailPanel>(window, "LastConnectionLine").IsWrapped);
    }
}
