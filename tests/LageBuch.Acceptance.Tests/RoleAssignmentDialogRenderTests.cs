using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The dialog for a Funktion already held: BEARBEITEN from FUNKTIONEN, ÄNDERN and ÜBERTRAGEN from a
// Führungsunterstützung on the board. Hosted directly, as LeadershipPersonDialogRenderTests does;
// the saved PNGs are the PR's screenshots.
public class RoleAssignmentDialogRenderTests
{
    private static readonly FixedClock Clock = new();

    private static readonly MasterDataSet MasterData = MasterDataSet.Empty with
    {
        Personnel =
        [
            new Person("Mustermann", "Max", "ZF", "Florian Emmering 11/1", "0151 2345678"),
        ],
    };

    private static LocalIncidentSession Session() =>
        TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Window Show(RoleAssignmentDialogViewModel vm, double width, double height)
    {
        var window = new Window { Content = new RoleAssignmentDialogView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void Bearbeiten_shows_the_recorded_details_and_the_abschnitt_of_a_typed_funktion()
    {
        var session = Session();
        session.AssignRole("Gruppenführer", "Musterfru, Erika", "Florian Emmering 40/1", section: "Wache Süd", phone: "0151 8765432");
        var vm = new RoleAssignmentDialogViewModel(session, MasterData, session.Incident.Roles[0].Id, RoleAssignmentDialogMode.Edit);
        var window = Show(vm, 560, 560);

        Assert.Equal("Gruppenführer bearbeiten", Find<TextBlock>(window, "AssignmentDialogTitle").Text);
        Assert.Equal("Musterfru, Erika", Find<AutoCompleteBox>(window, "AssignmentDialogNameBox").Text);
        Assert.Equal("Florian Emmering 40/1", Find<AutoCompleteBox>(window, "AssignmentDialogCallSignBox").Text);
        Assert.Equal("Wache Süd", Find<TextBox>(window, "AssignmentDialogSectionBox").Text);
        Assert.True(Find<TextBox>(window, "AssignmentDialogSectionBox").IsEffectivelyVisible);
        Assert.Equal("SPEICHERN", Find<Button>(window, "AssignmentDialogSaveButton").Content);
        Capture(window, "role-assignment-edit.png");
    }

    [AvaloniaFact]
    public void Uebertragen_from_the_board_renders_at_phone_width_without_an_abschnitt()
    {
        var session = Session();
        session.AssignRole("S2 Lage", "Musterfrau, Erika", slot: RoleSlot.Fuehrungsassistent);
        var vm = new RoleAssignmentDialogViewModel(session, MasterData, session.Incident.Roles[0].Id, RoleAssignmentDialogMode.Transfer)
        {
            PersonName = "Mustermann, Max",
        };
        var window = Show(vm, 412, 800);

        var card = Find<Border>(window, "AssignmentDialogCard");
        Assert.True(card.Bounds.Width <= 412 - 32 + 1);
        Assert.Equal("Übergabe S2 Lage: Musterfrau, Erika", Find<TextBlock>(window, "AssignmentDialogTitle").Text);
        Assert.Equal("Florian Emmering 11/1", Find<AutoCompleteBox>(window, "AssignmentDialogCallSignBox").Text);
        Assert.False(Find<HeaderedContentControl>(window, "AssignmentDialogSectionField").IsVisible);
        Assert.True(Find<Button>(window, "AssignmentDialogSaveButton").IsEffectivelyVisible);
        Capture(window, "role-assignment-transfer-narrow.png");
    }
}
