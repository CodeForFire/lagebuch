using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.Acceptance.Tests;

// The Abschnitt dialog behind the FÜHRUNG board. Hosted directly, same idiom as
// IncidentDataDialogRenderTests; the saved PNGs are the PR's "after" screenshots.
public class CommandNodeDialogRenderTests
{
    private static LocalIncidentSession Session() =>
        TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static MasterDataSet MasterData() =>
        MasterDataSet.Empty with
        {
            TalkGroups =
            [
                new TalkGroup("FW_EMMERING", "1000", TalkGroupKind.Tmo),
                new TalkGroup("FW_AICH", "1010", TalkGroupKind.Tmo),
                new TalkGroup("301_F", null, TalkGroupKind.Dmo),
            ],
        };

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static (Window Window, CommandNodeDialogViewModel Vm) Show(
        CommandNodeDialogViewModel vm, double width, double height)
    {
        var window = new Window { Content = new CommandNodeDialogView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static CommandNodeDialogViewModel Filled(IIncidentSession session)
    {
        var vm = CommandNodeDialogViewModel.ForCreate(session, MasterData(), CommandNodeKind.Abschnitt, null, "Abschnitt 1");
        vm.Name = "Abschnitt 1 Nord";
        vm.Auftrag = "Brandbekämpfung Nordseite, Riegelstellung zum Stallgebäude.";
        vm.Standort = "Hofeinfahrt Hauptstraße 12, Emmering";
        vm.Funkgruppe = "TMO FW_EMMERING 1000";
        vm.Rufname = "Florian Emmering 40/1";
        vm.Telefon = "0151 2345678";
        return vm;
    }

    [AvaloniaFact]
    public void Create_dialog_renders_at_desktop_width()
    {
        var (window, vm) = Show(Filled(Session()), 560, 760);

        Assert.Equal("Abschnitt 1 Nord", Find<TextBox>(window, "CommandNodeNameBox").Text);
        Assert.Equal("TMO FW_EMMERING 1000", Find<AutoCompleteBox>(window, "CommandNodeFunkgruppeBox").Text);
        Assert.Equal("BILDEN", Find<Button>(window, "CommandNodeSaveButton").Content);
        Assert.Equal("Abschnitt bilden", vm.Title);
        Assert.Equal(200, Find<TextBox>(window, "CommandNodeNameBox").MaxLength);
        Assert.Equal(1000, Find<TextBox>(window, "CommandNodeAuftragBox").MaxLength);
        Assert.Equal(50, Find<TextBox>(window, "CommandNodeTelefonBox").MaxLength);
        Capture(window, "abschnitt-dialog.png");
    }

    [AvaloniaFact]
    public void Create_dialog_renders_at_phone_width_without_clipping()
    {
        var (window, _) = Show(Filled(Session()), 412, 800);

        var card = Find<Border>(window, "CommandNodeDialogCard");
        Assert.True(card.Bounds.Width <= 412 - 32 + 1);
        Assert.True(Find<Button>(window, "CommandNodeSaveButton").IsEffectivelyVisible);
        Capture(window, "abschnitt-dialog-narrow.png");
    }

    [AvaloniaFact]
    public void Funkgruppe_field_lists_the_stammdaten_talk_groups()
    {
        var (window, _) = Show(Filled(Session()), 560, 760);

        var box = Find<AutoCompleteBox>(window, "CommandNodeFunkgruppeBox");
        Assert.Equal(["TMO FW_EMMERING 1000", "TMO FW_AICH 1010", "DMO 301_F"], box.ItemsSource!.Cast<string>());
    }

    [AvaloniaFact]
    public void The_post_dialog_hides_name_and_auftrag()
    {
        var session = Session();
        session.UpdateLeadershipPost("Feuerwehrhaus Emmering", "Florian Emmering", "08141 123456", "TMO FW_EMMERING 1000");
        var (window, _) = Show(CommandNodeDialogViewModel.ForPost(session, MasterData()), 560, 560);

        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBox>(),
            t => t.Name is "CommandNodeNameBox" or "CommandNodeAuftragBox" && t.IsEffectivelyVisible);
        Assert.Equal("Feuerwehrhaus Emmering", Find<TextBox>(window, "CommandNodeStandortBox").Text);
        window.AssertFocused(Find<TextBox>(window, "CommandNodeStandortBox"));
    }

    [AvaloniaFact]
    public void A_name_error_shows_under_the_field()
    {
        var session = Session();
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var vm = CommandNodeDialogViewModel.ForCreate(session, MasterData(), CommandNodeKind.Abschnitt, null, "Abschnitt 2");
        var (window, _) = Show(vm, 560, 760);

        Find<TextBox>(window, "CommandNodeNameBox").Text = "abschnitt 1";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Name ist bereits vergeben.", vm.NameError);
        Assert.False(Find<Button>(window, "CommandNodeSaveButton").Command!.CanExecute(null));
        Capture(window, "abschnitt-dialog-name-error.png");
    }

    [AvaloniaFact]
    public void Escape_cancels_when_untouched_and_asks_first_after_typing()
    {
        var closed = 0;
        var vm = CommandNodeDialogViewModel.ForCreate(Session(), MasterData(), CommandNodeKind.Abschnitt, null, "Abschnitt 1");
        vm.Closed += (_, _) => closed++;
        var (window, _) = Show(vm, 560, 760);
        Find<TextBox>(window, "CommandNodeNameBox").Focus();

        window.Press(PhysicalKey.Escape);
        Assert.Equal(1, closed);

        Find<TextBox>(window, "CommandNodeStandortBox").Text = "Hofeinfahrt";
        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, closed);
        Assert.True(Find<Border>(window, "CommandNodeDiscardPanel").IsEffectivelyVisible);
        window.AssertFocused(Find<Button>(window, "CommandNodeKeepEditingButton"));
    }

    [AvaloniaFact]
    public void Every_button_of_the_dialog_has_a_name_for_automation()
    {
        var (window, _) = Show(Filled(Session()), 560, 760);

        foreach (var button in window.GetVisualDescendants().OfType<Button>())
        {
            var named = !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) || button.Content is string { Length: > 0 };
            Assert.True(named, $"Button {button.Name} has neither text nor an automation name");
        }
    }
}
