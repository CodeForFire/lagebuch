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

namespace LageBuch.Acceptance.Tests;

// The person dialog behind the FÜHRUNG board (Leiter, Melder, Führungsassistenz, Fachberater).
// Hosted directly, same idiom as CommandNodeDialogRenderTests; the saved PNGs are the PR's
// "after" screenshots.
public class LeadershipPersonDialogRenderTests
{
    private static readonly FixedClock Clock = new();

    private static LocalIncidentSession Session() =>
        TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static MasterDataSet MasterData() =>
        MasterDataSet.Empty with
        {
            Roles =
            [
                new Role("Einsatzleiter", RoleUniqueness.UniquePerIncident, RoleSlot.Leiter),
                new Role("Abschnittsleiter", RoleUniqueness.UniquePerSection, RoleSlot.Leiter),
                new Role("Melder", RoleUniqueness.Multiple, RoleSlot.Melder),
                new Role("Fachberater THW", RoleUniqueness.Multiple, RoleSlot.Fachberater),
            ],
            Personnel =
            [
                new Person("Mustermann", "Max", "ZF", "Florian Emmering 11/1", "0151 2345678"),
                new Person("Musterfrau", "Erika", "GF", "Florian Emmering 40/1", "0151 8765432"),
            ],
        };

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static (Window Window, LeadershipPersonDialogViewModel Vm) Show(
        LeadershipPersonDialogViewModel vm, double width, double height)
    {
        var window = new Window { Content = new LeadershipPersonDialogView { DataContext = vm }, Width = width, Height = height };
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

    private static LeadershipPersonDialogViewModel LeiterFor(LocalIncidentSession session, out Guid nodeId)
    {
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        nodeId = session.Incident.CommandNodes[^1].Id;
        return new LeadershipPersonDialogViewModel(session, Clock, MasterData(), nodeId, RoleSlot.Leiter);
    }

    [AvaloniaFact]
    public void Leiter_dialog_renders_at_desktop_width_with_the_roster_prefilled()
    {
        var vm = LeiterFor(Session(), out _);
        vm.PersonName = "Mustermann, Max";
        var (window, _) = Show(vm, 560, 560);

        Assert.Equal("Leiter festlegen – Abschnitt 1 Nord", Find<TextBlock>(window, "PersonDialogTitle").Text);
        Assert.Equal("Abschnittsleiter", Find<AutoCompleteBox>(window, "PersonDialogRoleBox").Text);
        Assert.Equal("Florian Emmering 11/1", Find<AutoCompleteBox>(window, "PersonDialogCallSignBox").Text);
        Assert.Equal("0151 2345678", Find<TextBox>(window, "PersonDialogPhoneBox").Text);
        Assert.Equal("FESTLEGEN", Find<Button>(window, "PersonDialogSaveButton").Content);
        Assert.False(Find<TextBlock>(window, "PersonDialogHandoverText").IsVisible);
        Assert.Equal(200, Find<AutoCompleteBox>(window, "PersonDialogNameBox").MaxLength);
        Assert.Equal(50, Find<TextBox>(window, "PersonDialogPhoneBox").MaxLength);
        Capture(window, "person-dialog.png");
    }

    [AvaloniaFact]
    public void Fachberater_dialog_renders_at_phone_width_without_clipping()
    {
        var vm = new LeadershipPersonDialogViewModel(Session(), Clock, MasterData(), null, RoleSlot.Fachberater)
        {
            Role = "Fachberater THW",
            PersonName = "Musterfrau, Erika",
        };
        var (window, _) = Show(vm, 412, 800);

        var card = Find<Border>(window, "PersonDialogCard");
        Assert.True(card.Bounds.Width <= 412 - 32 + 1);
        Assert.True(Find<Button>(window, "PersonDialogSaveButton").IsEffectivelyVisible);
        Assert.Equal("HINZUFÜGEN", Find<Button>(window, "PersonDialogSaveButton").Content);
        Assert.Contains("Fachberater Polizei", Find<AutoCompleteBox>(window, "PersonDialogRoleBox").ItemsSource!.Cast<string>());
        Capture(window, "person-dialog-narrow.png");
    }

    [AvaloniaFact]
    public void A_handover_is_announced_above_the_buttons()
    {
        var session = Session();
        var vm = LeiterFor(session, out var nodeId);
        session.AssignRole("Abschnittsleiter", "Musterfrau, Erika", "Florian Emmering 40/1", from: Clock.Now, nodeId: nodeId, slot: RoleSlot.Leiter);
        vm.PersonName = "Mustermann, Max";
        var (window, _) = Show(vm, 560, 560);

        var hint = Find<TextBlock>(window, "PersonDialogHandoverText");
        Assert.True(hint.IsEffectivelyVisible);
        Assert.Equal("Übergabe: Musterfrau, Erika → neu", hint.Text);
        Capture(window, "person-dialog-handover.png");
    }

    [AvaloniaFact]
    public void A_funktion_recorded_elsewhere_is_announced_as_taken_over_here()
    {
        var session = Session();
        session.AssignRole("Einsatzleiter", "Mustermann, Max", "Florian Emmering 11/1", from: Clock.Now, phone: "0151 2345678");
        var vm = new LeadershipPersonDialogViewModel(session, Clock, MasterData(), null, RoleSlot.Leiter)
        {
            PersonName = "Mustermann, Max",
        };
        var (window, _) = Show(vm, 560, 560);

        var hint = Find<TextBlock>(window, "PersonDialogHandoverText");
        Assert.Equal("„Einsatzleiter“ (Mustermann, Max) wird hierher übernommen.", hint.Text);
        Assert.True(hint.IsEffectivelyVisible);
        Capture(window, "person-dialog-placement.png");
    }

    [AvaloniaFact]
    public void A_held_post_shows_the_holders_funktion_locked()
    {
        var session = Session();
        var vm = LeiterFor(session, out var nodeId);
        session.AssignRole("Abschnittsleiter", "Musterfrau, Erika", from: Clock.Now, nodeId: nodeId, slot: RoleSlot.Leiter);
        var (window, _) = Show(vm, 560, 560);

        var role = Find<AutoCompleteBox>(window, "PersonDialogRoleBox");
        Assert.False(role.IsEnabled);
        Assert.Equal("Abschnittsleiter", role.Text);
    }

    [AvaloniaFact]
    public void Funkrufname_offers_the_stammdaten_call_signs()
    {
        var (window, _) = Show(new LeadershipPersonDialogViewModel(Session(), Clock, MasterData(), null, RoleSlot.Fachberater), 560, 560);

        Assert.Equal(["Florian Emmering 11/1", "Florian Emmering 40/1"], Find<AutoCompleteBox>(window, "PersonDialogCallSignBox").ItemsSource!.Cast<string>());
    }

    [AvaloniaFact]
    public void Field_errors_show_under_their_fields()
    {
        var vm = new LeadershipPersonDialogViewModel(Session(), Clock, MasterData(), null, RoleSlot.Fachberater);
        var (window, _) = Show(vm, 560, 560);

        Find<Button>(window, "PersonDialogSaveButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var errors = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("field-error"))
            .Select(t => t.Text)
            .ToList();
        Assert.Contains("Funktion fehlt.", errors);
        Assert.Contains("Name fehlt.", errors);
    }

    [AvaloniaFact]
    public void Escape_cancels_when_untouched_and_asks_first_after_typing()
    {
        var closed = 0;
        var vm = new LeadershipPersonDialogViewModel(Session(), Clock, MasterData(), null, RoleSlot.Leiter);
        vm.Closed += (_, _) => closed++;
        var (window, _) = Show(vm, 560, 560);
        Find<AutoCompleteBox>(window, "PersonDialogCallSignBox").Focus();

        window.Press(PhysicalKey.Escape);
        Assert.Equal(1, closed);

        Find<AutoCompleteBox>(window, "PersonDialogCallSignBox").Text = "Florian Emmering 11/1";
        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, closed);
        Assert.True(Find<Border>(window, "PersonDialogDiscardPanel").IsEffectivelyVisible);
        window.AssertFocused(Find<Button>(window, "PersonDialogKeepEditingButton"));
    }
}
