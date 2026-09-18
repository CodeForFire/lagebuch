using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

internal static class WorkspaceRenderHelper
{
    public static MasterDataSet MasterData() => Md();

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplateAufbau = new[]
        {
            new ChecklistTemplateItem("Aufstellort ELW weit genug weg um nicht zu behindern?", true),
            new ChecklistTemplateItem("Bei BEIDEN Funkgeräten über die Bedienteile am Armaturenbrett die Lautstärke auf 0 gestellt?", false),
            new ChecklistTemplateItem("Rote Kennleuchte ein, Blaulicht aus?", false),
            new ChecklistTemplateItem("PC eingeschaltet und VPN Verbindung aktiviert?", false),
            new ChecklistTemplateItem("Kopfdaten ETB ausgefüllt (Einsatzort, Bearbeiter)?", false),
        },
        ChecklistTemplateAbbau = new[]
        {
            new ChecklistTemplateItem("Fahrzeug abgerüstet und einsatzbereit?", true),
        },
        TruppTypes = new[] { new TruppType("Angriffstrupp"), new TruppType("Sicherheitstrupp"), new TruppType("CSA-Trupp", 3, 20) },
        UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Bereitstellungsraum", "Im Einsatz" },

        // Wachen and Funkrufnamen suggestions derive from the vehicles (and the roster below).
        Vehicles = AnonymizedExampleData.Vehicles,
        Links = AnonymizedExampleData.Links,

        // Fictional roster: the real personnel.json is gitignored, so tests supply their own
        // (#137: same anonymized fixture the app's own placeholders are built from).
        Personnel = AnonymizedExampleData.Personnel,
    };

    public static IncidentWorkspaceViewModel BuildEditableWorkspaceWithAllBars(
        IIncidentHostController? host = null)
    {
        var clock = new FixedClock();
        var checklistAufbau = new[]
        {
            ("Aufstellort ELW weit genug weg um nicht zu behindern?", true),
            ("Bei BEIDEN Funkgeräten über die Bedienteile am Armaturenbrett die Lautstärke auf 0 gestellt?", false),
            ("Rote Kennleuchte ein, Blaulicht aus?", false),
            ("PC eingeschaltet und VPN Verbindung aktiviert?", false),
            ("Kopfdaten ETB ausgefüllt (Einsatzort, Bearbeiter)?", false),
        };
        var checklistAbbau = new[] { ("Fahrzeug abgerüstet und einsatzbereit?", true) };

        // Keyword + Einsatznummer both set: the common post-#69 shape once ILS has called back --
        // Stichwort as the header hero, the Einsatznummer as the secondary chip beside it.
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            checklistAufbau,
            checklistAbbau,
            new IncidentNumber("B 1.2 260715 123"),
            keyword: "B3P");
        var ticker = new ManualTicker();
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            Md(),
            new FakeDialogs(),
            new NoopAlarmService(),
            host ?? new NoopIncidentHostController());

        // Drive the three header bars into their visible states (like the reported screenshot):
        //   1) ILS reminder running, 2) SCBA pressure-control due, 3) Rückzugsalarm active.
        // The ILS reminder auto-starts with the incident (no manual start).
        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.OperatorSurname;
        vm.Scba.NewTruppmann = AnonymizedExampleData.OperatorSurnameAlt;
        vm.Scba.AddTruppCommand.Execute(null);
        var row = vm.Scba.Trupps[^1];
        row.StartCommand.Execute(null);

        // Advance past the 30-min max duration so the trupp is in Rückzugsalarm.
        clock.Now = clock.Now.AddMinutes(31);
        ticker.Pulse();

        // The advance also carried the auto-started reminder past its first interval; acknowledge it
        // so the bar shows a running countdown (not "fällig") for the composite screenshot.
        vm.Reminder!.AcknowledgeCommand.Execute(null);

        return vm;
    }

    /// <summary>The workspace's left nav rail.</summary>
    public static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    /// <summary>
    /// Selects the rail tab whose rendered header reads <paramref name="header"/>, pumping the
    /// dispatcher so that tab's content is realized.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>SelectedIndex = n</c>. The rail's contents are becoming Stammdaten --
    /// 0..n user-defined checklists, plus per-module visibility -- so a positional literal stops
    /// naming the tab it was written for, and a stale index fails as a wrong-tab assertion rather
    /// than as a missing tab. Matching on the header survives that.
    /// <para>
    /// Reading the first TextBlock under the container is safe for either header shape (a bare
    /// string, or the checklists' TextBlock-plus-status-dots panel): TabControl hoists the
    /// selected tab's content into PART_SelectedContentHost, so a TabItem's own visual subtree
    /// holds nothing but its header.
    /// </para>
    /// </remarks>
    public static TabControl SelectTab(Window window, string header)
    {
        var tabs = Tabs(window);
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            if (HeaderTextOf(tabs.ContainerFromIndex(i)) == header)
            {
                tabs.SelectedIndex = i;
                Dispatcher.UIThread.RunJobs();
                return tabs;
            }
        }

        var shown = string.Join(
            ", ",
            Enumerable.Range(0, tabs.ItemCount)
                .Select(i => HeaderTextOf(tabs.ContainerFromIndex(i)) ?? "?"));
        throw new InvalidOperationException(
            $"no rail tab headed \"{header}\" -- the rail shows: {shown}.");
    }

    /// <summary>The realized visual of the selected tab's content.</summary>
    /// <remarks>
    /// <c>TabControl.SelectedContent</c> is the selected <em>item</em>, which is a Visual only
    /// while the tabs are literal TabItems carrying literal content. Taking the presenter's
    /// realized child instead keeps working once the rail is ItemsSource-driven and the items are
    /// view models.
    /// </remarks>
    public static Visual SelectedTabContent(Window window)
    {
        var host = Tabs(window).GetVisualDescendants().OfType<ContentPresenter>()
            .First(p => p.Name == "PART_SelectedContentHost");
        return host.Child
            ?? throw new InvalidOperationException("the selected rail tab has no realized content.");
    }

    /// <summary>Every rail tab's rendered header, in rail order.</summary>
    public static IReadOnlyList<string> RailHeaders(Window window)
    {
        var tabs = Tabs(window);
        return Enumerable.Range(0, tabs.ItemCount)
            .Select(i => HeaderTextOf(tabs.ContainerFromIndex(i)) ?? "?")
            .ToArray();
    }

    private static string? HeaderTextOf(Control? container) =>
        container?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
}

internal sealed class ManualTicker : ITicker
{
    private readonly List<Action> _subs = new();

    public IDisposable Subscribe(Action onTick)
    {
        _subs.Add(onTick);
        return new Sub();
    }

    public void Pulse()
    {
        foreach (var s in _subs.ToArray())
        {
            s();
        }
    }

    private sealed class Sub : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
